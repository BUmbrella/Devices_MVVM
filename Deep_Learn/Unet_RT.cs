using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using JYPPX.TensorRtSharp.Cuda;
using JYPPX.TensorRtSharp.Nvinfer;
using OpenCvSharp;

namespace Devices_MVVM.Deep_Learn
{
    /// <summary>
    /// TensorRT 版 UNet 分割推理类。
    /// 支持单张 Predict(Mat)，也支持多张 PredictBatch(IReadOnlyList&lt;Mat&gt;)。
    /// 输入预处理参考 Python 版本：BGR -> RGB、resize 到 256x256、/255、HWC -> CHW。
    /// 输出后处理参考 Python 版本：对 [N,2,256,256] 做 argmax，得到类别 mask，再 resize 回原图尺寸。
    /// </summary>
    public sealed class Unet_RT : IDisposable
    {
        private const int InputWidth = 256;
        private const int InputHeight = 256;
        private const int ClassNum = 2;
        private const string InputTensorName = "images";
        private const string OutputTensorName = "output0";

        // mask 上每个类别显示什么颜色。
        // 这里保持 Python 版本一致：0 是黑色背景，1 是红色前景。
        private static readonly Scalar[] ClassColors =
        {
            new Scalar(0, 0, 0),
            new Scalar(255, 0, 0)
        };

        // LabelMe JSON 里类别 id 到 label 文本的映射。
        // Python 版本里 class 1 的 label 是 "0"。
        private static readonly Dictionary<int, string> IdToLabel = new Dictionary<int, string>
        {
            [1] = "0"
        };

        private readonly object _predictLock = new object();
        private readonly Runtime _runtime;
        private readonly CudaEngine _cudaEngine;
        private readonly JYPPX.TensorRtSharp.Nvinfer.ExecutionContext _executionContext;
        private readonly CudaStream _cudaStream;
        private readonly LogCallbackFunction _callbackDelegate;
        private readonly bool _isDynamicBatch;
        private readonly int _fixedBatch;
        private bool _disposed;

        public Unet_RT(string enginePath)
        {
            _callbackDelegate = message => Debug.WriteLine(message);
            Logger.Instance.SetCallback(_callbackDelegate);
            Logger.Instance.SetThreshold(LoggerSeverity.kINFO);

            string resolvedEnginePath = Path.IsPathRooted(enginePath)
                ? enginePath
                : Path.Combine(AppContext.BaseDirectory, enginePath);

            if (!File.Exists(resolvedEnginePath))
                throw new FileNotFoundException("TensorRT engine file not found.", resolvedEnginePath);

            byte[] engineData = File.ReadAllBytes(resolvedEnginePath);

            _runtime = new Runtime();
            _cudaEngine = _runtime.deserializeCudaEngineByBlob(engineData, (ulong)engineData.Length);
            _executionContext = _cudaEngine.createExecutionContext(TrtExecutionContextAllocationStrategy.kSTATIC);
            _cudaStream = new CudaStream();

            Dims inputDims = _executionContext.getTensorShape(InputTensorName);
            _isDynamicBatch = inputDims.d[0] <= 0;
            _fixedBatch = _isDynamicBatch ? 0 : (int)inputDims.d[0];

            // 动态 batch engine 在执行前必须指定真实输入形状。
            // 构造函数里先用 batch=1 初始化一次，方便预热。
            if (_isDynamicBatch)
            {
                _executionContext.setinputShape(InputTensorName, new Dims(1, 3, InputHeight, InputWidth));
            }

            using Cuda1DMemory<float> warmInput = new Cuda1DMemory<float>((ulong)(3 * InputHeight * InputWidth));
            using Cuda1DMemory<float> warmOutput = new Cuda1DMemory<float>((ulong)(ClassNum * InputHeight * InputWidth));
            _executionContext.setInputTensorAddress(InputTensorName, warmInput.get());
            _executionContext.setOutputTensorAddress(OutputTensorName, warmOutput.get());
            _executionContext.executeV3(_cudaStream);
            _cudaStream.Synchronize();
        }

        /// <summary>
        /// 单张图分割。返回的 Mask 尺寸和原图一致，类型是 CV_8UC1，像素值就是类别 id。
        /// </summary>
        public SegmentationResult Predict(Mat image)
        {
            IReadOnlyList<SegmentationResult> results = PredictBatch(new[] { image });
            if (results.Count == 0)
                throw new InvalidOperationException("UNet predict returned empty result.");

            return results[0];
        }

        /// <summary>
        /// 多张图分割。
        /// 动态 batch engine 会按 images.Count 设置真实 batch，比如 [3,3,256,256]。
        /// 固定 batch engine 会按固定 batch 执行，只把真实图片填到前面，其余位置保持 0。
        /// </summary>
        public IReadOnlyList<SegmentationResult> PredictBatch(IReadOnlyList<Mat> images)
        {
            if (images == null || images.Count == 0)
                return Array.Empty<SegmentationResult>();

            lock (_predictLock)
            {
                ThrowIfDisposed();

                // TensorRT profile 最大 batch 是 32，所以这里限制输入数量。
                int requestedBatch = Math.Clamp(images.Count, 1, 32);
                int actualBatch = _isDynamicBatch ? requestedBatch : _fixedBatch;
                int realCount = Math.Min(images.Count, actualBatch);

                if (_isDynamicBatch)
                {
                    // 动态 batch 每次推理前都要设置本次真实 shape。
                    _executionContext.setinputShape(InputTensorName, new Dims(actualBatch, 3, InputHeight, InputWidth));
                }

                // 输入 Tensor 布局是 NCHW：[batch, 3, 256, 256]。
                // 每张图占 3*256*256 个 float。
                float[] inputData = new float[actualBatch * 3 * InputHeight * InputWidth];

                // 输出 Tensor 布局通常是 [batch, class, 256, 256]。
                // class=2，对每个像素输出两个类别分数。
                float[] outputData = new float[actualBatch * ClassNum * InputHeight * InputWidth];
                List<OriginalImageInfo> infos = new List<OriginalImageInfo>();

                for (int i = 0; i < realCount; i++)
                {
                    if (images[i] == null || images[i].Empty())
                        continue;

                    infos.Add(new OriginalImageInfo(images[i].Width, images[i].Height, i));
                    PreProcess(images[i], inputData, i);
                }

                using Cuda1DMemory<float> inputGpuMemory = new Cuda1DMemory<float>((ulong)inputData.Length);
                using Cuda1DMemory<float> outputGpuMemory = new Cuda1DMemory<float>((ulong)outputData.Length);

                _executionContext.setInputTensorAddress(InputTensorName, inputGpuMemory.get());
                _executionContext.setOutputTensorAddress(OutputTensorName, outputGpuMemory.get());

                inputGpuMemory.copyFromHostAsync(inputData, _cudaStream);
                _executionContext.executeV3(_cudaStream);
                _cudaStream.Synchronize();
                outputGpuMemory.copyToHostAsync(outputData, _cudaStream);
                _cudaStream.Synchronize();

                List<SegmentationResult> results = new List<SegmentationResult>();
                foreach (OriginalImageInfo info in infos)
                {
                    // smallMask 是 256x256 的类别图。
                    using Mat smallMask = ArgmaxMask(outputData, info.BatchIndex);

                    // Python 版本最后把 256x256 mask resize 回原图大小。
                    // mask 是类别 id，不是普通图像，所以必须用最近邻插值，避免出现 0.3、0.7 这种无意义类别。
                    Mat originalMask = new Mat();
                    Cv2.Resize(
                        smallMask,
                        originalMask,
                        new Size(info.Width, info.Height),
                        0,
                        0,
                        InterpolationFlags.Nearest);

                    results.Add(new SegmentationResult(originalMask));
                }

                return results;
            }
        }

        /// <summary>
        /// 保存彩色 mask。
        /// 输入 mask 是类别图；输出是 RGB 颜色图，保存时会自动转成 OpenCV 需要的 BGR。
        /// </summary>
        public static void SaveColorMask(Mat mask, string savePath)
        {
            using Mat colorMaskRgb = MaskToColor(mask);
            using Mat colorMaskBgr = new Mat();
            Cv2.CvtColor(colorMaskRgb, colorMaskBgr, ColorConversionCodes.RGB2BGR);
            Cv2.ImWrite(savePath, colorMaskBgr);
        }

        /// <summary>
        /// 保存 LabelMe JSON。
        /// 逻辑和 Python 版本一致：对每个目标类别找轮廓，只保留面积最大的区域。
        /// </summary>
        public static void SaveLabelMeJson(Mat mask, string imagePath, string savePath)
        {
            List<LabelMeShape> shapes = new List<LabelMeShape>();

            foreach ((int classId, string label) in IdToLabel)
            {
                using Mat binary = new Mat();

                // 生成二值图：mask 中等于 classId 的像素为 255，其它为 0。
                // 后续 FindContours 只能在二值图上找轮廓。
                Cv2.Compare(mask, classId, binary, CmpTypes.EQ);
                Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                if (contours.Length == 0)
                    continue;

                // 如果同一类有多个区域，只保留面积最大的区域。
                Point[] maxContour = contours
                    .OrderByDescending(contour => Cv2.ContourArea(contour))
                    .First();

                // polygon 至少需要 3 个点。
                if (maxContour.Length < 3)
                    continue;

                List<double[]> points = maxContour
                    .Select(point => new[] { (double)point.X, (double)point.Y })
                    .ToList();

                shapes.Add(new LabelMeShape(
                    label,
                    points,
                    null,
                    "polygon",
                    new Dictionary<string, object>()));
            }

            using Mat image = Cv2.ImRead(imagePath);
            var json = new LabelMeDocument(
                "5.0.1",
                new Dictionary<string, object>(),
                shapes,
                Path.GetFileName(imagePath),
                null,
                image.Rows,
                image.Cols);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            File.WriteAllText(savePath, JsonSerializer.Serialize(json, options));
        }

        private static unsafe void PreProcess(Mat image, float[] chwData, int batchIndex)
        {
            // Python 版本使用 cv2.imread 读 BGR，然后 cv2.cvtColor 转 RGB。
            // 这里保持一致，保证通道顺序和训练时相同。
            using Mat rgb = new Mat();
            Cv2.CvtColor(image, rgb, ColorConversionCodes.BGR2RGB);

            // UNet 训练和导出时使用 256x256 输入，所以推理也统一 resize 到 256x256。
            using Mat resized = new Mat();
            Cv2.Resize(rgb, resized, new Size(InputWidth, InputHeight), 0, 0, InterpolationFlags.Linear);

            int channelSize = InputWidth * InputHeight;

            // batchBase 是当前图片在 NCHW 大数组中的起始位置。
            // 第 0 张图从 0 开始，第 1 张图从 1*3*256*256 开始。
            int batchBase = batchIndex * 3 * channelSize;
            const float inv255 = 1.0f / 255.0f;

            byte* srcBase = (byte*)resized.DataPointer;
            long srcStep = resized.Step();

            for (int y = 0; y < InputHeight; y++)
            {
                byte* row = srcBase + y * srcStep;
                int rowOffset = y * InputWidth;

                for (int x = 0; x < InputWidth; x++)
                {
                    // RGB 图像仍然是 HWC 排布：一个像素连续三个 byte。
                    int srcIdx = x * 3;
                    int dstIdx = rowOffset + x;

                    // 一次完成 /255 归一化和 HWC -> CHW。
                    // Python 对应代码：
                    // img_resized.transpose(2,0,1).float().unsqueeze(0) / 255.0
                    chwData[batchBase + dstIdx] = row[srcIdx + 0] * inv255;
                    chwData[batchBase + channelSize + dstIdx] = row[srcIdx + 1] * inv255;
                    chwData[batchBase + channelSize * 2 + dstIdx] = row[srcIdx + 2] * inv255;
                }
            }
        }

        private static Mat ArgmaxMask(float[] outputData, int batchIndex)
        {
            Mat mask = new Mat(InputHeight, InputWidth, MatType.CV_8UC1, Scalar.All(0));
            int maskSize = InputHeight * InputWidth;

            // 输出布局是 [N, C, H, W] 展平。
            // 当前图片的起始位置是 batchIndex * C * H * W。
            int batchBase = batchIndex * ClassNum * maskSize;

            for (int y = 0; y < InputHeight; y++)
            {
                for (int x = 0; x < InputWidth; x++)
                {
                    int pixelIndex = y * InputWidth + x;

                    // 对当前像素，在所有类别分数里找最大值。
                    // ClassNum=2 时，就是比较 background 和 foreground 谁更大。
                    int bestClass = 0;
                    float bestScore = outputData[batchBase + pixelIndex];

                    for (int classId = 1; classId < ClassNum; classId++)
                    {
                        float score = outputData[batchBase + classId * maskSize + pixelIndex];
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestClass = classId;
                        }
                    }

                    mask.Set(y, x, (byte)bestClass);
                }
            }

            return mask;
        }

        private static Mat MaskToColor(Mat mask)
        {
            Mat colorMask = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC3, Scalar.All(0));

            for (int classId = 0; classId < ClassColors.Length; classId++)
            {
                using Mat binary = new Mat();
                Cv2.Compare(mask, classId, binary, CmpTypes.EQ);
                colorMask.SetTo(ClassColors[classId], binary);
            }

            return colorMask;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Unet_RT));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _cudaStream.Dispose();
            _executionContext.Dispose();
            _cudaEngine.Dispose();
            _runtime.Dispose();
            _disposed = true;
        }

        public sealed record SegmentationResult(Mat Mask);

        private sealed record OriginalImageInfo(int Width, int Height, int BatchIndex);

        private sealed record LabelMeShape(
            string Label,
            List<double[]> Points,
            object? GroupId,
            string ShapeType,
            Dictionary<string, object> Flags);

        private sealed record LabelMeDocument(
            string Version,
            Dictionary<string, object> Flags,
            List<LabelMeShape> Shapes,
            string ImagePath,
            string? ImageData,
            int ImageHeight,
            int ImageWidth);
    }
}
