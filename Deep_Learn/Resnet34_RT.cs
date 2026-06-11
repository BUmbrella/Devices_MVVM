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
    /// TensorRT ResNet34 分类推理类。
    /// 支持动态 batch engine：例如输入 shape 为 [-1,3,224,224] 时
    /// </summary>
    public sealed class Resnet34_RT : IDisposable
    {
        private const int InputWidth = 224;
        private const int InputHeight = 224;
        private const string InputTensorName = "images";
        private const string OutputTensorName = "output0";

        // 训练时如果不是 ImageNet mean/std，这里必须同步修改。
        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

        private readonly object _predictLock = new object();
        private readonly Runtime _runtime;
        private readonly CudaEngine _cudaEngine;
        private readonly JYPPX.TensorRtSharp.Nvinfer.ExecutionContext _executionContext;
        private readonly CudaStream _cudaStream;
        private readonly LogCallbackFunction _callbackDelegate;
        private readonly bool _isDynamicBatch;
        private readonly int _fixedBatch;
        private readonly int _categoryNum;
        private bool _disposed;

        public Resnet34_RT(string enginePath)
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

            // 动态 engine 在构造阶段先用 batch=1 初始化一次，方便读取输出类别数和预热。
            if (_isDynamicBatch)
            {
                _executionContext.setinputShape(InputTensorName, new Dims(1, 3, InputHeight, InputWidth));
            }

            Dims outputDims = _executionContext.getTensorShape(OutputTensorName);
            _categoryNum = outputDims.nbDims >= 2 ? (int)outputDims.d[1] : (int)outputDims.d[0];

            using Cuda1DMemory<float> warmInput = new Cuda1DMemory<float>((ulong)(3 * InputHeight * InputWidth));
            using Cuda1DMemory<float> warmOutput = new Cuda1DMemory<float>((ulong)_categoryNum);
            _executionContext.setInputTensorAddress(InputTensorName, warmInput.get());
            _executionContext.setOutputTensorAddress(OutputTensorName, warmOutput.get());
            //预热一次
            _executionContext.executeV3(_cudaStream);
            _cudaStream.Synchronize();
        }

        /// <summary>
        /// 单张图分类。返回 TopK 结果。
        /// </summary>
        public ClassificationResult[] Predict(Mat image, int topK = 5, bool applySoftmax = true)
        {
            IReadOnlyList<ClassificationResult[]> results = PredictBatch(new[] { image }, topK, applySoftmax);
            return results.Count == 0 ? Array.Empty<ClassificationResult>() : results[0];
        }

        /// <summary>
        /// 多张图批量分类。
        /// 动态 batch engine 会按 images.Count 设置真实 batch；
        /// 固定 batch engine 会填入前 N 张，其余位置保持 0，但推理仍按固定 batch 计算。
        /// </summary>
        public IReadOnlyList<ClassificationResult[]> PredictBatch(
            IReadOnlyList<Mat> images,
            int topK = 5,
            bool applySoftmax = true)
        {
            if (images == null || images.Count == 0)
                return Array.Empty<ClassificationResult[]>();

            lock (_predictLock)
            {
                ThrowIfDisposed();

                // requestedBatch 是这次调用希望送进模型的图片数量。
                // 为什么要限制到 1~32：你的动态 engine 是按 trtexec profile 构建的，
                // minShapes=1、maxShapes=32，超出 32 就不在 engine 支持范围内。
                int requestedBatch = Math.Clamp(images.Count, 1, 32);

                // actualBatch 是这次真正给 TensorRT 分配和执行的 batch。
                // 动态 batch engine：actualBatch 等于本次图片数量，例如 3 张就是 3。
                // 固定 batch engine：actualBatch 等于 engine 写死的 batch，例如 32；即使只传 3 张，也会按 32 计算。
                int actualBatch = _isDynamicBatch ? requestedBatch : _fixedBatch;

                // realImageCount 是实际会被写入输入 Tensor 的图片数量。
                // 如果固定 batch engine 是 32，但只传了 3 张，则只填前 3 个位置，其余位置保持 0。
                int realImageCount = Math.Min(images.Count, actualBatch);

                if (_isDynamicBatch)
                {
                    // 动态 batch engine 在每次推理前必须设置真实输入形状。
                    // 例如本次 images.Count=3，则设置为 [3,3,224,224]。
                    // 如果不设置，TensorRT 不知道本次到底要跑几张图。
                    _executionContext.setinputShape(InputTensorName, new Dims(actualBatch, 3, InputHeight, InputWidth));
                }

                // inputData 是 CPU 端输入 Tensor，布局是 NCHW：
                // [batch, channel, height, width]。
                // 数组展开后，每一张图占 3*224*224 个 float。
                // 第 0 张图起始位置：0
                // 第 1 张图起始位置：1 * 3*224*224
                // 第 2 张图起始位置：2 * 3*224*224
                float[] inputData = new float[actualBatch * 3 * InputHeight * InputWidth];

                // outputData 是 CPU 端输出 Tensor，布局通常是 [batch, categoryNum]。
                // 例如 batch=3、类别数=21，则输出长度是 3*21。
                float[] outputData = new float[actualBatch * _categoryNum];

                for (int i = 0; i < realImageCount; i++)
                {
                    if (images[i] == null || images[i].Empty())
                        continue;

                    // 把第 i 张 Mat 写到 inputData 的第 i 个 batch 位置。
                    // 这样 TensorRT 输出的第 i 行结果就对应输入的第 i 张图。
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

                List<ClassificationResult[]> batchResults = new List<ClassificationResult[]>();
                for (int i = 0; i < realImageCount; i++)
                {
                    // outputData 里每张图都有一段分类输出。
                    // batchIndex=i 表示取第 i 张图对应的类别分数，再做 TopK。
                    batchResults.Add(PostProcessTopK(outputData, _categoryNum, i, topK, applySoftmax));
                }

                return batchResults;
            }
        }

        private static unsafe void PreProcess(Mat img, float[] chwData, int batchIndex)
        {
            // ResNet34 训练/推理要求输入尺寸固定。
            // 这里统一 resize 到 224x224，保证输入 Tensor 形状和 engine 的 [N,3,224,224] 匹配。
            // 如果训练时用了其它预处理方式，例如先缩放短边再中心裁剪，推理时也应该保持一致。
            using Mat resizedImg = new Mat();
            Cv2.Resize(img, resizedImg, new Size(InputWidth, InputHeight), 0, 0, InterpolationFlags.Linear);

            // channelSize 表示单通道像素数量，也就是 224*224。
            // CHW 布局里：
            // R 通道占 [0, channelSize)
            // G 通道占 [channelSize, 2*channelSize)
            // B 通道占 [2*channelSize, 3*channelSize)
            int channelSize = InputWidth * InputHeight;

            // batchBase 是当前图片在整个 batch 输入数组里的起始位置。
            // 每张图占 3*channelSize 个 float。
            // 例如 batchIndex=2，说明这是第 3 张图，要从 2*3*224*224 的位置开始写。
            int batchBase = batchIndex * 3 * channelSize;

            // 像素从 byte 的 0~255 归一化到 float 的 0~1。
            const float inv255 = 1.0f / 255.0f;

            // OpenCV Mat 默认是连续/按行存储的 HWC BGR byte 数据。
            // DataPointer 是图像数据起始地址；Step 是每一行实际占用字节数。
            // 用指针访问可以避免 safe API 每个像素的额外开销。
            byte* srcBase = (byte*)resizedImg.DataPointer;
            long srcStep = resizedImg.Step();

            for (int y = 0; y < InputHeight; y++)
            {
                // 找到第 y 行的起始地址。
                byte* row = srcBase + y * srcStep;

                // rowOffset 是当前像素在单通道平面里的行偏移。
                int rowOffset = y * InputWidth;

                for (int x = 0; x < InputWidth; x++)
                {
                    // OpenCV 的三通道 Mat 是 HWC 排布：
                    // 第 x 个像素的三个 byte 连在一起，所以偏移是 x*3。
                    int srcIdx = x * 3;

                    // dstIdx 是当前像素在一个 CHW 通道平面中的位置。
                    int dstIdx = rowOffset + x;

                    // OpenCV 默认通道顺序是 BGR，不是 RGB。
                    // 但大多数 PyTorch/torchvision 训练的 ResNet 输入是 RGB。
                    byte b = row[srcIdx + 0];
                    byte g = row[srcIdx + 1];
                    byte r = row[srcIdx + 2];

                    // 这里一次性完成三件事：
                    // 1. BGR -> RGB：写入顺序按 R、G、B。
                    // 2. HWC -> CHW：同一个通道的所有像素连续存储。
                    // 3. Normalize：先除以 255 得到 0~1，再减 mean、除 std。
                    //
                    // 为什么要 Normalize：
                    // 训练 ResNet 时通常使用 torchvision 的 Normalize(mean,std)，
                    // 推理时必须使用相同的均值和方差，否则输入分布和训练时不同，分类结果会变差。
                    chwData[batchBase + dstIdx] = (r * inv255 - Mean[0]) / Std[0];
                    chwData[batchBase + channelSize + dstIdx] = (g * inv255 - Mean[1]) / Std[1];
                    chwData[batchBase + channelSize * 2 + dstIdx] = (b * inv255 - Mean[2]) / Std[2];
                }
            }
        }

        private static ClassificationResult[] PostProcessTopK(
            float[] logits,
            int categoryNum,
            int batchIndex,
            int topK,
            bool applySoftmax)
        {
            // TensorRT 输出通常是展平的一维数组。
            // 对分类模型来说，逻辑形状一般是 [batch, categoryNum]。
            // 第 batchIndex 张图的输出起始位置是 batchIndex * categoryNum。
            // 这里先把当前图片对应的 categoryNum 个分数拷出来，避免后续排序误读其它 batch 的结果。
            float[] currentLogits = new float[categoryNum];
            Array.Copy(logits, batchIndex * categoryNum, currentLogits, 0, categoryNum);

            // logits 是模型原始输出，数值不一定是 0~1，也不一定总和为 1。
            // 如果 engine 最后一层没有 Softmax，就需要在 CPU 后处理里转成概率。
            // 如果你的 engine 已经包含 Softmax 层，可以传 applySoftmax=false，避免重复 softmax。
            float[] scores = applySoftmax ? Softmax(currentLogits) : currentLogits;

            // order 存类别索引，不存分数本身。
            // 例如 order=[0,1,2,...,20]，排序后可能变成 [11,12,4,...]，
            // 表示类别 11 分数最高，类别 12 第二高。
            List<int> order = new List<int>(scores.Length);

            for (int i = 0; i < scores.Length; i++)
            {
                order.Add(i);
            }

            // 按分数从高到低排序。
            // a、b 是类别索引，scores[a] / scores[b] 才是对应分数。
            order.Sort((a, b) => scores[b].CompareTo(scores[a]));

            // topK 不能超过类别数量。
            // 例如类别数 21，topK=5，则只取前 5 个最高分类别。
            int count = Math.Min(topK, order.Count);
            ClassificationResult[] results = new ClassificationResult[count];

            for (int i = 0; i < count; i++)
            {
                // classId 是真实类别 ID，score 是这个类别对应的概率/分数。
                int classId = order[i];
                results[i] = new ClassificationResult(classId, scores[classId]);
            }

            return results;
        }

        private static float[] Softmax(float[] logits)
        {
            // Softmax 公式：
            // exp(x_i) / sum(exp(x_j))
            // 直接 exp(logits[i]) 可能数值溢出，所以先减掉最大值 max。
            // 这个操作不会改变 softmax 结果，但会让计算更稳定。
            float max = float.NegativeInfinity;
            for (int i = 0; i < logits.Length; i++)
            {
                if (logits[i] > max)
                    max = logits[i];
            }

            // 计算每个类别的 exp(logit - max)，并累加总和。
            float sum = 0f;
            float[] probabilities = new float[logits.Length];
            for (int i = 0; i < logits.Length; i++)
            {
                float value = MathF.Exp(logits[i] - max);
                probabilities[i] = value;
                sum += value;
            }

            // 除以总和后，每个值就变成概率，所有类别概率加起来约等于 1。
            // 加 1e-6f 是防止极端情况下 sum 为 0 导致除零。
            float invSum = 1.0f / (sum + 1e-6f);
            for (int i = 0; i < probabilities.Length; i++)
            {
                probabilities[i] *= invSum;
            }

            return probabilities;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Resnet34_RT));
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

        public sealed record ClassificationResult(int ClassId, float Score);
    }
}
