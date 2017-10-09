﻿using System;
using System.Collections.Generic;
using System.Drawing;
using Cloo;
using KelpNet.Common;
using KelpNet.Common.Functions;
using KelpNet.Common.Tools;

namespace KelpNet.Functions.Connections
{
    [Serializable]
    public class Deconvolution2D : CompressibleFunction
    {
        const string FUNCTION_NAME = "Deconvolution2D";
        private const string PARAM_NAME = "/*ForwardActivate*/";
        private const string PARAM_VALUE = "ForwardActivate(gpuY + outputIndex);";

        private readonly List<Real[]> _prevOutput = new List<Real[]>();

        public NdArray Weight;
        public NdArray Bias;

        public readonly bool NoBias;

        private readonly int _kWidth;
        private readonly int _kHeight;
        private readonly int _subSampleX;
        private readonly int _subSampleY;
        private readonly int _trimX;
        private readonly int _trimY;

        public readonly int InputCount;
        public readonly int OutputCount;

        public Deconvolution2D(int inputChannels, int outputChannels, int kSize, int subSample = 1, int trim = 0, bool noBias = false, Array initialW = null, Array initialb = null, string name = FUNCTION_NAME, bool gpuEnable = false, CompressibleActivation activation = null) : base(name, gpuEnable, FUNCTION_NAME, activation, new KeyValuePair<string, string>(PARAM_NAME, PARAM_VALUE))
        {
            this._kWidth = kSize;
            this._kHeight = kSize;
            this._trimX = trim;
            this._trimY = trim;
            this._subSampleX = subSample;
            this._subSampleY = subSample;
            this.NoBias = noBias;

            this.Parameters = new NdArray[noBias ? 1 : 2];

            this.OutputCount = outputChannels;
            this.InputCount = inputChannels;

            this.Initialize(initialW, initialb);
        }

        public Deconvolution2D(int inputChannels, int outputChannels, Size kSize, Size subSample = new Size(), Size trim = new Size(), bool noBias = false, Array initialW = null, Array initialb = null, string name = FUNCTION_NAME, bool gpuEnable = false, CompressibleActivation activation = null) : base(name, gpuEnable, FUNCTION_NAME, activation, new KeyValuePair<string, string>(PARAM_NAME, PARAM_VALUE))
        {
            if (subSample == Size.Empty)
                subSample = new Size(1, 1);

            if (trim == Size.Empty)
                trim = new Size(0, 0);

            this._kWidth = kSize.Width;
            this._kHeight = kSize.Height;
            this._trimX = trim.Width;
            this._trimY = trim.Height;
            this.NoBias = noBias;

            this._subSampleX = subSample.Width;
            this._subSampleY = subSample.Height;

            this.Parameters = new NdArray[noBias ? 1 : 2];

            this.OutputCount = outputChannels;
            this.InputCount = inputChannels;

            this.Initialize(initialW, initialb);
        }

        void Initialize(Array initialW = null, Array initialb = null)
        {
            this.Weight = new NdArray(OutputCount, InputCount, this._kHeight, this._kWidth);

            if (initialW == null)
            {
                Initializer.InitWeight(this.Weight);
            }
            else
            {
                this.Weight.Data = Real.GetArray(initialW);
            }

            this.Parameters[0] = this.Weight;


            if (!NoBias)
            {
                this.Bias = new NdArray(OutputCount);

                if (initialb != null)
                {
                    this.Bias.Data = Real.GetArray(initialb);
                }

                this.Parameters[1] = this.Bias;
            }
        }

        protected override NdArray NeedPreviousForwardCpu(NdArray input)
        {
            int outputHeight = (input.Shape[1] - 1) * this._subSampleY + this._kHeight - this._trimY * 2;
            int outputWidth = (input.Shape[2] - 1) * this._subSampleX + this._kWidth - this._trimX * 2;

            Real[] result = new Real[input.BatchCount * this.OutputCount * outputWidth * outputHeight];

            int outSizeOffset = outputWidth * outputHeight;
            int inputSizeOffset = input.Shape[1] * input.Shape[2];
            int kSizeOffset = this.Weight.Shape[2] * this.Weight.Shape[3];

            for (int batchCount = 0; batchCount < input.BatchCount; batchCount++)
            {
                for (int och = 0; och < this.OutputCount; och++)
                {
                    for (int oy = this._trimY; oy < outputHeight + this._trimY; oy++)
                    {
                        int iyLimit = oy / this._subSampleY + 1 < input.Shape[1] ? oy / this._subSampleY + 1 : input.Shape[1];
                        int iyStart = oy - this.Weight.Shape[2] < 0 ? 0 : (oy - this.Weight.Shape[2]) / this._subSampleY + 1;

                        for (int ox = this._trimX; ox < outputWidth + this._trimX; ox++)
                        {
                            int ixLimit = ox / this._subSampleX + 1 < input.Shape[2] ? ox / this._subSampleX + 1 : input.Shape[2];
                            int ixStart = ox - this.Weight.Shape[3] < 0 ? 0 : (ox - this.Weight.Shape[3]) / this._subSampleX + 1;

                            int outputIndex = batchCount * this.OutputCount * outSizeOffset + och * outSizeOffset + (oy - this._trimY) * outputWidth + ox - this._trimX;

                            for (int ich = 0; ich < input.Shape[0]; ich++)
                            {
                                int inputIndexOffset = batchCount * input.Length + ich * inputSizeOffset;
                                int kernelIndexOffset = och * this.Weight.Shape[1] * kSizeOffset + ich * kSizeOffset;

                                for (int iy = iyStart; iy < iyLimit; iy++)
                                {
                                    for (int ix = ixStart; ix < ixLimit; ix++)
                                    {
                                        int inputIndex = inputIndexOffset + iy * input.Shape[2] + ix;
                                        int kernelIndex = kernelIndexOffset + (oy - iy * this._subSampleY) * this.Weight.Shape[3] + (ox - ix * this._subSampleX);

                                        result[outputIndex] += input.Data[inputIndex] * this.Weight.Data[kernelIndex];
                                    }
                                }
                            }

                        }
                    }
                }
            }

            if (this.Activation != null && !NoBias)
            {
                for (int batchCount = 0; batchCount < input.BatchCount; batchCount++)
                {
                    for (int och = 0; och < this.OutputCount; och++)
                    {
                        for (int oy = this._trimY; oy < outputHeight + this._trimY; oy++)
                        {
                            for (int ox = this._trimX; ox < outputWidth + this._trimX; ox++)
                            {
                                int outputIndex = batchCount * this.OutputCount * outSizeOffset + och * outSizeOffset + (oy - this._trimY) * outputWidth + ox - this._trimX;

                                result[outputIndex] += this.Bias.Data[och];
                                this.Activation.ForwardActivate(ref result[outputIndex]);
                            }
                        }
                    }
                }
            }
            else if (!NoBias)
            {
                for (int batchCount = 0; batchCount < input.BatchCount; batchCount++)
                {
                    for (int och = 0; och < this.OutputCount; och++)
                    {
                        for (int oy = this._trimY; oy < outputHeight + this._trimY; oy++)
                        {
                            for (int ox = this._trimX; ox < outputWidth + this._trimX; ox++)
                            {
                                int outputIndex = batchCount * this.OutputCount * outSizeOffset + och * outSizeOffset + (oy - this._trimY) * outputWidth + ox - this._trimX;

                                result[outputIndex] += this.Bias.Data[och];
                            }
                        }
                    }
                }
            }
            else if (this.Activation != null)
            {
                for (int batchCount = 0; batchCount < input.BatchCount; batchCount++)
                {
                    for (int och = 0; och < this.OutputCount; och++)
                    {
                        for (int oy = this._trimY; oy < outputHeight + this._trimY; oy++)
                        {
                            for (int ox = this._trimX; ox < outputWidth + this._trimX; ox++)
                            {
                                int outputIndex = batchCount * this.OutputCount * outSizeOffset + och * outSizeOffset + (oy - this._trimY) * outputWidth + ox - this._trimX;

                                this.Activation.ForwardActivate(ref result[outputIndex]);
                            }
                        }
                    }
                }
            }

            return GetForwardResult(result, outputWidth, outputHeight, input.BatchCount);
        }

        protected override NdArray NeedPreviousForwardGpu(NdArray input)
        {
            int outputHeight = (input.Shape[1] - 1) * this._subSampleY + this._kHeight - this._trimY * 2;
            int outputWidth = (input.Shape[2] - 1) * this._subSampleX + this._kWidth - this._trimX * 2;

            Real[] result = new Real[input.BatchCount * this.OutputCount * outputWidth * outputHeight];

            using (ComputeBuffer<Real> gpuX = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, input.Data))
            using (ComputeBuffer<Real> gpuW = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, this.Weight.Data))
            using (ComputeBuffer<Real> gpub = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, this.Bias.Data))
            using (ComputeBuffer<Real> gpuY = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.AllocateHostPointer, result.Length))
            {
                ForwardKernel.SetMemoryArgument(0, gpuX);
                ForwardKernel.SetMemoryArgument(1, gpuW);
                ForwardKernel.SetMemoryArgument(2, gpub);
                ForwardKernel.SetMemoryArgument(3, gpuY);
                ForwardKernel.SetValueArgument(4, input.Shape[1]);
                ForwardKernel.SetValueArgument(5, input.Shape[2]);
                ForwardKernel.SetValueArgument(6, input.Length);
                ForwardKernel.SetValueArgument(7, outputWidth);
                ForwardKernel.SetValueArgument(8, outputHeight);
                ForwardKernel.SetValueArgument(9, this._subSampleX);
                ForwardKernel.SetValueArgument(10, this._subSampleY);
                ForwardKernel.SetValueArgument(11, this._trimX);
                ForwardKernel.SetValueArgument(12, this._trimY);
                ForwardKernel.SetValueArgument(13, this._kHeight);
                ForwardKernel.SetValueArgument(14, this._kWidth);
                ForwardKernel.SetValueArgument(15, this.OutputCount);
                ForwardKernel.SetValueArgument(16, this.InputCount);

                Weaver.CommandQueue.Execute
                    (
                        ForwardKernel,
                        null,
                        new long[] { input.BatchCount * OutputCount, outputHeight, outputWidth },
                        null,
                        null
                    );

                Weaver.CommandQueue.Finish();
                Weaver.CommandQueue.ReadFromBuffer(gpuY, ref result, true, null);
            }

            return GetForwardResult(result, outputWidth, outputHeight, input.BatchCount);
        }

        NdArray GetForwardResult(Real[] y, int outputWidth, int outputHeight, int batchCount)
        {
            NdArray output = NdArray.Convert(y, new[] { this.OutputCount, outputHeight, outputWidth }, batchCount);

            if (this.Activation != null)
            {
                this._prevOutput.Add(output.Data);
            }

            return output;
        }

        Real[] GetActivatedgy(NdArray gy)
        {
            int gyIndex = 0;

            Real[] activatedgy = new Real[gy.BatchCount * gy.Length];

            var prevOutputData = this._prevOutput[this._prevOutput.Count - 1];
            this._prevOutput.RemoveAt(this._prevOutput.Count - 1);

            for (int batchCounter = 0; batchCounter < gy.BatchCount; batchCounter++)
            {
                for (int och = 0; och < gy.Shape[0]; och++)
                {
                    for (int olocation = 0; olocation < gy.Shape[1] * gy.Shape[2]; olocation++)
                    {
                        Real gyData = gy.Data[gyIndex];

                        this.Activation.BackwardActivate(ref gyData, prevOutputData[gyIndex]);

                        activatedgy[gyIndex] = gyData;

                        gyIndex++;
                    }
                }
            }

            return activatedgy;
        }

        void CalcBiasGrad(Real[] gy, int[] gyShape, int batchCount)
        {
            int gyIndex = 0;

            for (int batchCounter = 0; batchCounter < batchCount; batchCounter++)
            {
                for (int och = 0; och < gyShape[0]; och++)
                {
                    for (int olocation = 0; olocation < gyShape[1] * gyShape[2]; olocation++)
                    {
                        this.Bias.Grad[och] += gy[gyIndex];

                        gyIndex++;
                    }
                }
            }
        }

        protected override NdArray NeedPreviousBackwardCpu(NdArray gy, NdArray x)
        {
            Real[] gx = new Real[x.Data.Length];
            Real[] activatedgy = this.Activation != null ? GetActivatedgy(gy) : gy.Data;
            if (!NoBias) CalcBiasGrad(activatedgy, gy.Shape, gy.BatchCount);

            //本来のロジック
            for (int batchCount = 0; batchCount < gy.BatchCount; batchCount++)
            {
                for (int och = 0; och < OutputCount; och++)
                {
                    int outChOffset = och * this.Weight.Shape[1] * this.Weight.Shape[2] * this.Weight.Shape[3];
                    int inputOffset = och * gy.Shape[1] * gy.Shape[2];

                    for (int oy = this._trimY; oy < gy.Shape[1] + this._trimY; oy++)
                    {
                        int iyLimit = oy / this._subSampleY + 1 < x.Shape[1] ? oy / this._subSampleY + 1 : x.Shape[1];
                        int iyStart = oy - this.Weight.Shape[2] < 0 ? 0 : (oy - this.Weight.Shape[2]) / this._subSampleY + 1;

                        for (int ox = this._trimX; ox < gy.Shape[2] + this._trimX; ox++)
                        {
                            int ixLimit = ox / this._subSampleX + 1 < x.Shape[2] ? ox / this._subSampleX + 1 : x.Shape[2];
                            int ixStart = ox - this.Weight.Shape[3] < 0 ? 0 : (ox - this.Weight.Shape[3]) / this._subSampleX + 1;

                            int gyIndex = batchCount * gy.Length + inputOffset + (oy - this._trimY) * gy.Shape[2] + ox - this._trimX;
                            Real gyData = activatedgy[gyIndex];

                            for (int ich = 0; ich < InputCount; ich++)
                            {
                                int inChOffset = outChOffset + ich * this.Weight.Shape[2] * this.Weight.Shape[3];
                                int pinputOffset = batchCount * x.Length + ich * x.Shape[1] * x.Shape[2];

                                for (int iy = iyStart; iy < iyLimit; iy++)
                                {
                                    for (int ix = ixStart; ix < ixLimit; ix++)
                                    {
                                        int pInIndex = pinputOffset + iy * x.Shape[2] + ix;
                                        int gwIndex = inChOffset + (oy - iy * this._subSampleY) * this.Weight.Shape[3] + (ox - ix * this._subSampleX);

                                        this.Weight.Data[gwIndex] += x.Data[pInIndex] * gyData;
                                        gx[pInIndex] += this.Weight.Data[gwIndex] * gyData;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return NdArray.Convert(gx, x.Shape, x.BatchCount);
        }

        protected override NdArray NeedPreviousBackwardGpu(NdArray gy, NdArray x)
        {
            Real[] gx = new Real[x.Data.Length];
            Real[] activatedgy = this.Activation != null ? GetActivatedgy(gy) : gy.Data;
            if (!NoBias) CalcBiasGrad(activatedgy, gy.Shape, gy.BatchCount);

            //gyは共通で使用
            using (ComputeBuffer<Real> gpugY = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, activatedgy))
            {
                using (ComputeBuffer<Real> gpugW = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, this.Weight.Grad))
                using (ComputeBuffer<Real> gpuX = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, x.Data))
                {
                    this.BackwardgWKernel.SetMemoryArgument(0, gpugY);
                    this.BackwardgWKernel.SetMemoryArgument(1, gpuX);
                    this.BackwardgWKernel.SetMemoryArgument(2, gpugW);
                    this.BackwardgWKernel.SetValueArgument(3, gy.BatchCount);
                    this.BackwardgWKernel.SetValueArgument(4, this.InputCount);
                    this.BackwardgWKernel.SetValueArgument(5, gy.Length);
                    this.BackwardgWKernel.SetValueArgument(6, gy.Shape[1]);
                    this.BackwardgWKernel.SetValueArgument(7, gy.Shape[2]);
                    this.BackwardgWKernel.SetValueArgument(8, x.Shape[1]);
                    this.BackwardgWKernel.SetValueArgument(9, x.Shape[2]);
                    this.BackwardgWKernel.SetValueArgument(10, x.Length);
                    this.BackwardgWKernel.SetValueArgument(11, this._subSampleX);
                    this.BackwardgWKernel.SetValueArgument(12, this._subSampleY);
                    this.BackwardgWKernel.SetValueArgument(13, this._trimX);
                    this.BackwardgWKernel.SetValueArgument(14, this._trimY);
                    this.BackwardgWKernel.SetValueArgument(15, this._kHeight);
                    this.BackwardgWKernel.SetValueArgument(16, this._kWidth);

                    Weaver.CommandQueue.Execute
                    (
                        this.BackwardgWKernel,
                        null,
                        new long[] { OutputCount * InputCount, this._kHeight, this._kWidth },
                        null,
                        null
                    );

                    Weaver.CommandQueue.Finish();
                    Weaver.CommandQueue.ReadFromBuffer(gpugW, ref this.Weight.Grad, true, null);
                }

                using (ComputeBuffer<Real> gpugX = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.AllocateHostPointer, gx.Length))
                using (ComputeBuffer<Real> gpuW = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, this.Weight.Data))
                {
                    this.BackwardgXKernel.SetMemoryArgument(0, gpugY);
                    this.BackwardgXKernel.SetMemoryArgument(1, gpuW);
                    this.BackwardgXKernel.SetMemoryArgument(2, gpugX);
                    this.BackwardgXKernel.SetValueArgument(3, this.OutputCount);
                    this.BackwardgXKernel.SetValueArgument(4, this.InputCount);
                    this.BackwardgXKernel.SetValueArgument(5, gy.Length);
                    this.BackwardgXKernel.SetValueArgument(6, gy.Shape[1]);
                    this.BackwardgXKernel.SetValueArgument(7, gy.Shape[2]);
                    this.BackwardgXKernel.SetValueArgument(8, x.Shape[1]);
                    this.BackwardgXKernel.SetValueArgument(9, x.Shape[2]);
                    this.BackwardgXKernel.SetValueArgument(10, x.Length);
                    this.BackwardgXKernel.SetValueArgument(11, this._subSampleX);
                    this.BackwardgXKernel.SetValueArgument(12, this._subSampleY);
                    this.BackwardgXKernel.SetValueArgument(13, this._trimX);
                    this.BackwardgXKernel.SetValueArgument(14, this._trimY);
                    this.BackwardgXKernel.SetValueArgument(15, this._kHeight);
                    this.BackwardgXKernel.SetValueArgument(16, this._kWidth);

                    Weaver.CommandQueue.Execute
                    (
                        this.BackwardgXKernel,
                        null,
                        new long[] { gy.BatchCount * x.Shape[0], x.Shape[1], x.Shape[2] },
                        null,
                        null
                    );

                    Weaver.CommandQueue.Finish();
                    Weaver.CommandQueue.ReadFromBuffer(gpugX, ref gx, true, null);
                }
            }

            return NdArray.Convert(gx, x.Shape, x.BatchCount);
        }
    }
}
