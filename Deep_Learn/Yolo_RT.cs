using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JYPPX.TensorRtSharp.Cuda;
using JYPPX.TensorRtSharp.Nvinfer;
using OpenCvSharp;

namespace Devices_MVVM.Deep_Learn
{
    /// <summary>
    /// TensorRT 版 YOLO 推理类。
    ///
    /// 从 C:\Users\PC\source\repos\推理\Program.cs 移植而来，但这里不再写成控制台 Main，
    /// 而是封装成 WPF/MVVM 项目里可以复用的类：
    /// 1. 构造函数加载 TensorRT engine，并分配 GPU 输入/输出显存。
    /// 2. Predict(Mat img) 接收 OpenCV 图像，返回检测框结果。
    /// 3. 前处理、后处理、同类别 NMS、IoU 和绘图函数都保留，并保留原来的解释性注释。
    /// </summary>
    public sealed class Yolo_RT : IDisposable
    {
        // ================= 配置参数 =================
        // 模型输入尺寸 (宽=高)
        private const int InputSize = 640;

        // 建议根据实际模型动态获取或使用 Netron 查看(也就是算出来8400个候选目标框)
        private const int OutputSize = 8400;

        // yolo有三个检测尺度 8 16 32
        // 检测的尺寸=(模型输入尺寸/8)^2 + (模型输入尺寸/16)^2 + (模型输入尺寸/32)^2
        // 640 输入时就是 80*80 + 40*40 + 20*20 = 8400。

        // 模型类别数。
        private const int CategoryNum = 1;

        // 置信度阈值
        private const float ConfThreshold = 0.25f;

        // NMS IOU 阈值
        private const float NmsThreshold = 0.3f;

        // TensorRT engine 里的输入张量名称。
        private const string InputTensorName = "images";

        // TensorRT engine 里的输出张量名称。
        private const string OutputTensorName = "output0";

        // 推理锁。
        private readonly object _predictLock = new object();

        // TensorRT Runtime。
        // 作用：负责反序列化 engine，创建 CudaEngine。
        private readonly Runtime _runtime;

        // TensorRT CudaEngine。
        // 作用：保存反序列化后的网络结构、权重、优化后的执行计划。
        private readonly CudaEngine _cudaEngine;

        // TensorRT 执行上下文。
        // 作用：真正执行推理的上下文，里面记录 tensor 绑定、动态 shape、执行状态等。
        private readonly JYPPX.TensorRtSharp.Nvinfer.ExecutionContext _executionContext;

        // CUDA 流。
        // 作用：管理 GPU 上异步任务的执行顺序，比如 Host->Device 拷贝、TensorRT 推理、Device->Host 拷贝。
        private readonly CudaStream _cudaStream;

        // 输入 GPU 显存。
        // 作用：存放模型输入 Tensor，格式是 [1,3,640,640] 的 float 数据。
        private readonly Cuda1DMemory<float> _inputGpuMemory;

        // 输出 GPU 显存。
        // 作用：TensorRT 推理完成后，把 [1,4+类别数,8400] 的结果写到这里。
        private readonly Cuda1DMemory<float> _outputGpuMemory;

        // CPU 端输入缓冲区。
        // 作用：PreProcess 把 OpenCV Mat 转成 YOLO 需要的 RGB/CHW/float 数据后写到这里。
        private readonly float[] _inputData;

        // CPU 端输出缓冲区。
        // 作用：把 TensorRT 输出从 GPU 拷回 CPU 后放到这里，后处理再从这个数组解析框、类别和置信度。
        //复用输出数组，避免每次 Predict 分配 outputChannels*8400 个 float。
        private readonly float[] _outputData;

        // 输出通道数。
        // YOLO 检测输出通道 = 4 个坐标(cx,cy,w,h) + 类别数。
        // 例如 1 类就是 5，80 类就是 84。
        private readonly int _outputChannels;

        // TensorRT 日志回调委托。
        // 为什么保存成字段：如果只用局部变量，可能被 GC 回收；native 层后续回调时会有风险。
        // 保存成字段可以保证推理器生命周期内回调一直有效。
        private readonly LogCallbackFunction _callbackDelegate;

        // 资源释放标记。
        // 作用：防止 Dispose 被重复执行，也防止对象释放后继续 Predict。
        private bool _disposed;

        /// <summary>
        /// 创建 TensorRT YOLO 推理器。
        /// </summary>
        /// <param name="enginePath">
        /// TensorRT engine 路径。
        /// 如果传相对路径，就按 AppContext.BaseDirectory 运行目录查找。
        /// </param>
        public Yolo_RT(string enginePath = "yolov8s.engine")
        {
            // ============= 配置 TensorRT 日志回调 =============
            // 定义一个委托，用于处理 TensorRT 内部产生的日志消息。
            // 这允许我们将 C++ 层面的日志输出到 Debug 窗口，避免 WPF 项目依赖控制台。
            _callbackDelegate = message => Debug.WriteLine(message);

            // 将自定义的回调函数注册给 TensorRT 的全局 Logger 实例。
            Logger.Instance.SetCallback(_callbackDelegate);

            // 设置日志的严重性级别阈值。
            // LoggerSeverity.kINFO: 打印信息、警告和错误。
            // 开发调试阶段通常设为 kINFO 或 kVERBOSE；生产环境可设为 kWARNING 或 kERROR 以减少输出。
            Logger.Instance.SetThreshold(LoggerSeverity.kINFO);

            string resolvedEnginePath = Path.IsPathRooted(enginePath)
                ? enginePath
                : Path.Combine(AppContext.BaseDirectory, enginePath);

            if (!File.Exists(resolvedEnginePath))
                throw new FileNotFoundException("TensorRT engine file not found.", resolvedEnginePath);

            // ================= 1. 加载 TensorRT Engine =================
            // 使用 using 语句确保文件流正确关闭。
            byte[] engineData;
            using (FileStream fs = new FileStream(resolvedEnginePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                engineData = br.ReadBytes((int)fs.Length);
            }

            // 反序列化 Engine。
            // Runtime 必须在 Engine 生命周期内保持存活，所以这里保存为字段，在 Dispose 里最后释放。
            _runtime = new Runtime();
            _cudaEngine = _runtime.deserializeCudaEngineByBlob(engineData, (ulong)engineData.Length);

            // ================= 2. 初始化推理上下文与显存 =================
            // 创建执行上下文。
            _executionContext = _cudaEngine.createExecutionContext(TrtExecutionContextAllocationStrategy.kSTATIC);
            _cudaStream = new CudaStream(); // 创建 CUDA 流用于异步执行。

            // 获取输入维度信息 (用于校验)。
            Dims inputDims = _executionContext.getTensorShape(InputTensorName);
            Debug.WriteLine($"Input Shape: {inputDims.d[0]}x{inputDims.d[1]}x{inputDims.d[2]}x{inputDims.d[3]}");

            // 计算所需显存大小。
            // 输入: Batch=1, Channel=3, Height=640, Width=640。
            ulong inputSizeInBytes = 1 * 3 * InputSize * InputSize;

            // 输出: Batch=1, Channels=CategoryNum+4(box), Num=8400。
            // 输出数据的格式 cx, cy, w, h, class0, class1, class2, ..., classN。
            // 前四个是框的位置数据，后面是各个类别分数。
            _outputChannels = CategoryNum + 4; // 4坐标 + N类别。
            ulong outputSizeInBytes = (ulong)(1 * _outputChannels * OutputSize);

            // 分配 GPU 显存。
            _inputGpuMemory = new Cuda1DMemory<float>(inputSizeInBytes);
            _outputGpuMemory = new Cuda1DMemory<float>(outputSizeInBytes);

            // 绑定显存地址到 TensorRT 上下文。
            _executionContext.setInputTensorAddress(InputTensorName, _inputGpuMemory.get());
            _executionContext.setOutputTensorAddress(OutputTensorName, _outputGpuMemory.get());

            _inputData = new float[3 * InputSize * InputSize];
            _outputData = new float[_outputChannels * OutputSize];

            // 预热推理。
            // 为什么需要：首次 executeV3 可能包含一些初始化开销，预热后正式推理计时更稳定。
            _executionContext.executeV3(_cudaStream);
            _cudaStream.Synchronize();
        }

        /// <summary>
        /// 对一张 OpenCV Mat 执行 YOLO TensorRT 推理。
        /// </summary>
        public List<DetData> Predict(Mat img)
        {
            if (img == null || img.Empty())
                return new List<DetData>();

            lock (_predictLock)
            {
                ThrowIfDisposed();

                // ================= 3. 图像预处理 =================
                // 预处理会把 OpenCV 的 BGR HWC byte 图像，变成 YOLO 需要的 RGB CHW float[]。
                PreProcess(img, _inputData, out float scale, out int xOffset, out int yOffset);

                // ================= 4. 推理 =================
                // 将数据从主机拷贝到设备。
                _inputGpuMemory.copyFromHostAsync(_inputData, _cudaStream);

                // 执行推理 (enqueueV3 是异步的)。
                _executionContext.executeV3(_cudaStream);

                // 等待推理完成。
                _cudaStream.Synchronize();

                // 将结果从设备拷贝回主机。
                // 这里的拷贝是同步等待后的输出读取。
                _outputGpuMemory.copyToHostAsync(_outputData, _cudaStream);

                // ================= 5. 后处理 =================
                return PostProcess(_outputData, scale, xOffset, yOffset);
            }
        }

        /// <summary>
        /// 图像预处理：Letterbox 缩放、归一化、HWC 转 CHW。
        /// </summary>
        private static unsafe void PreProcess(Mat img, float[] chwData, out float scale, out int xOffset, out int yOffset)
        {
            // 计算 Letterbox 缩放比例。
            // 为什么不先 BGR->RGB：颜色转换可以在最后写 Tensor 时顺手完成，避免整张图多跑一次 CvtColor。
            int maxDim = Math.Max(img.Width, img.Height);
            scale = (float)maxDim / InputSize;

            int newWidth = (int)(img.Width / scale);
            int newHeight = (int)(img.Height / scale);

            using Mat resizedImg = new Mat();
            Cv2.Resize(img, resizedImg, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);

            xOffset = (InputSize - newWidth) / 2;
            yOffset = (InputSize - newHeight) / 2;

            // 如果这里每次输入的照片是一样的大小，这里都不用去清空padding，因为空的地方每次都是无数据。
            // 直接填充最终 CHW Tensor。
            // 为什么只清 padding：数组会在循环推理中复用，但 resized 图覆盖的区域马上会被新像素写满；只有 letterbox 留白区域需要清零，避免残留上一帧内容。
            // 这样比每帧 Array.Clear 整个 3x640x640 float 数组少做很多无用内存写入。
            ClearLetterboxPadding(chwData, newWidth, newHeight, xOffset, yOffset);

            int channelSize = InputSize * InputSize; // resize后一张照片有多少个像素。
            const float inv255 = 1.0f / 255.0f; // 归一化系数。

            // 拿到 resizedImg 图像数据的起始地址。
            byte* srcBase = (byte*)resizedImg.DataPointer;

            // 图像每行占多少个字节。
            long srcStep = resizedImg.Step();

            // 归一化。
            for (int y = 0; y < newHeight; y++)
            {
                byte* row = srcBase + y * srcStep;
                int dstRowOffset = (y + yOffset) * InputSize + xOffset;

                for (int x = 0; x < newWidth; x++)
                {
                    int srcIdx = x * 3;
                    int dstIdx = dstRowOffset + x;

                    byte b = row[srcIdx + 0];
                    byte g = row[srcIdx + 1];
                    byte r = row[srcIdx + 2];

                    // HWC 转 CHW，同时 BGR 转 RGB，并把 0~255 归一化到 0~1。
                    chwData[dstIdx] = r * inv255;
                    chwData[channelSize + dstIdx] = g * inv255;
                    chwData[channelSize * 2 + dstIdx] = b * inv255;
                }
            }
        }

        /// <summary>
        /// 只清理 letterbox padding 区域，保留马上会被 resized 图覆盖的区域。
        /// </summary>
        private static void ClearLetterboxPadding(float[] chwData, int contentWidth, int contentHeight, int xOffset, int yOffset)
        {
            int channelSize = InputSize * InputSize;
            int rightOffset = xOffset + contentWidth;
            int bottomOffset = yOffset + contentHeight;

            for (int c = 0; c < 3; c++)
            {
                int channelBase = c * channelSize;

                if (yOffset > 0)
                {
                    Array.Clear(chwData, channelBase, yOffset * InputSize);
                }

                if (bottomOffset < InputSize)
                {
                    Array.Clear(chwData, channelBase + bottomOffset * InputSize, (InputSize - bottomOffset) * InputSize);
                }

                if (xOffset > 0 || rightOffset < InputSize)
                {
                    for (int y = yOffset; y < bottomOffset; y++)
                    {
                        int rowBase = channelBase + y * InputSize;

                        if (xOffset > 0)
                        {
                            Array.Clear(chwData, rowBase, xOffset);
                        }

                        if (rightOffset < InputSize)
                        {
                            Array.Clear(chwData, rowBase + rightOffset, InputSize - rightOffset);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 后处理：解析 TensorRT 输出、NMS 过滤。
        /// </summary>
        private static List<DetData> PostProcess(float[] result, float scale, int xOffset, int yOffset)
        {
            List<Rect> boxes = new List<Rect>();
            List<float> confidences = new List<float>();
            List<int> classIds = new List<int>();

            // 遍历所有预测框 (OutputSize)。
            // 数据布局: [4(box) + N(classes)] * OutputSize。
            // 展平数组中，同一属性的数据是连续存储的，例如所有 cx 在一起，所有 cy 在一起。
            int stride = OutputSize; // 步长，不同属性在数组中的偏移量。

            for (int i = 0; i < OutputSize; i++)
            {
                // 查找最大类别概率及其索引。
                float maxConf = 0;
                int maxClassId = -1;

                // 遍历类别。
                for (int c = 0; c < CategoryNum; c++)
                {
                    // 数组索引：(坐标偏移量 + 类别偏移) * 框索引。
                    // 数据排布是8400个cx，然后8400个cy，每个类的置信度在它自己的类别顺序之前加4。
                    float conf = result[(4 + c) * stride + i];
                    if (conf > maxConf)
                    {
                        maxConf = conf;
                        maxClassId = c;
                    }
                }

                // 置信度过滤。
                if (maxConf > ConfThreshold)
                {
                    // 提取坐标 (cx, cy, w, h)。
                    float cx = result[0 * stride + i];
                    float cy = result[1 * stride + i];
                    float w = result[2 * stride + i];
                    float h = result[3 * stride + i];

                    // 还原坐标到原图尺寸。
                    int rx = (int)((cx - xOffset - 0.5 * w) * scale);
                    int ry = (int)((cy - yOffset - 0.5 * h) * scale);
                    int rw = (int)(w * scale);
                    int rh = (int)(h * scale);

                    boxes.Add(new Rect(rx, ry, rw, rh));
                    confidences.Add(maxConf);
                    classIds.Add(maxClassId);
                }
            }

            // 执行同类别 NMS。
            // 同类别：IoU 超过阈值时只保留置信度高的框。
            // 不同类别：即使重叠也不互相抑制。
            int[] indices;
            NMS_Cheeck(boxes, confidences, classIds, ConfThreshold, NmsThreshold, out indices);

            List<DetData> finalResults = new List<DetData>();
            foreach (int idx in indices)
            {
                finalResults.Add(new DetData
                {
                    index = classIds[idx],
                    score = confidences[idx],
                    box = boxes[idx]
                });
            }

            return finalResults;
        }

        /// <summary>
        /// 同类别互相抑制，不同类别互不影响。
        /// </summary>
        public static void NMS_Cheeck(
            List<Rect> boxes,
            List<float> scores,
            List<int> classIds,
            float scoreThreshold,
            float nmsThreshold,
            out int[] indices)
        {
            if (boxes == null)
                throw new ArgumentNullException(nameof(boxes));

            if (scores == null)
                throw new ArgumentNullException(nameof(scores));

            if (classIds == null)
                throw new ArgumentNullException(nameof(classIds));

            if (boxes.Count != scores.Count || boxes.Count != classIds.Count)
                throw new ArgumentException("boxes, scores, classIds 的数量必须一致。");

            List<int> order = new List<int>();

            // 先直接筛掉置信度过低的。
            for (int i = 0; i < boxes.Count; i++)
            {
                if (scores[i] >= scoreThreshold)
                {
                    order.Add(i);
                }
            }

            // 先按置信度从高到低排序。
            // 为什么需要排序：NMS 默认先信高分框，后面的同类重叠低分框会被抑制。
            order.Sort((a, b) => scores[b].CompareTo(scores[a]));

            bool[] suppressed = new bool[boxes.Count];
            List<int> keep = new List<int>();

            for (int i = 0; i < order.Count; i++)
            {
                int bestIndex = order[i];
                if (suppressed[bestIndex])
                    continue;

                keep.Add(bestIndex);

                for (int j = i + 1; j < order.Count; j++)
                {
                    int currentIndex = order[j];
                    if (suppressed[currentIndex])
                        continue;

                    // 不同类别不互相抑制。
                    if (classIds[bestIndex] != classIds[currentIndex])
                        continue;

                    // 同类别且重叠过高，说明大概率是同一目标的重复框。
                    // 因为 bestIndex 分数更高，所以抑制 currentIndex。
                    if (RectIoU(boxes[bestIndex], boxes[currentIndex]) > nmsThreshold)
                    {
                        suppressed[currentIndex] = true; // 就把这个框看作是已经确认过了。
                    }
                }
            }

            indices = keep.ToArray();
        }

        private static float RectIoU(Rect a, Rect b)
        {
            // 得到交集框左上角。
            int xx1 = Math.Max(a.X, b.X);
            int yy1 = Math.Max(a.Y, b.Y);

            // 得到交集框右下角。
            int xx2 = Math.Min(a.X + a.Width, b.X + b.Width);
            int yy2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            int w = Math.Max(0, xx2 - xx1); // 如果等于0，则认为它们本身就没有相交，完全错开。
            int h = Math.Max(0, yy2 - yy1);
            float intersection = w * h;

            float areaA = Math.Max(0, a.Width) * Math.Max(0, a.Height);
            float areaB = Math.Max(0, b.Width) * Math.Max(0, b.Height);

            return intersection / (areaA + areaB - intersection + 1e-6f); // 防止除以0。
        }

        /// <summary>
        /// 绘制检测结果（水平矩形框）。
        /// </summary>
        /// <param name="results">检测结果列表</param>
        /// <param name="image">原始图像</param>
        /// <returns>绘制后的图像</returns>
        public static Mat DrawDetResult(List<DetData> results, Mat image)
        {
            // 克隆图像以免修改原图。
            Mat mat = image.Clone();

            foreach (var item in results)
            {
                // 1. 绘制矩形框。
                // Rect 结构包含 X, Y, Width, Height。
                Cv2.Rectangle(mat, item.box, new Scalar(0, 255, 0), thickness: 2);

                // 2. 准备标签文本 (类别ID - 置信度)。
                string label = $"{item.index} - {item.score:F2}";

                // 3. 计算文本的尺寸，用于绘制背景。
                int baseLine = 1;
                Size textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.6, 1, out baseLine);

                // 注意：这里简化处理，直接画在框上方。
                Cv2.Rectangle(
                    mat,
                    new Point(item.box.X, item.box.Y - textSize.Height - 5),
                    new Point(item.box.X + textSize.Width, item.box.Y),
                    new Scalar(0, 255, 0),
                    thickness: -1); // -1 表示填充。

                // 4. 绘制文本。
                Cv2.PutText(
                    mat,
                    label,
                    new Point(item.box.X, item.box.Y - 5),
                    HersheyFonts.HersheySimplex,
                    0.6,
                    new Scalar(0, 0, 0),
                    1);
            }

            return mat;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Yolo_RT));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _outputGpuMemory.Dispose();
            _inputGpuMemory.Dispose();
            _cudaStream.Dispose();
            _executionContext.Dispose();
            _cudaEngine.Dispose();
            _runtime.Dispose();
            _disposed = true;
        }

        public class DetData
        {
            public int index;
            public float score;
            public Rect box;
        }
    }
}
