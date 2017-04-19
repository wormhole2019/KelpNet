﻿using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using KelpNet.Common.Tools;

namespace KelpNet.Common
{
    //NumpyのNdArrayを模したクラス
    //N次元のArrayクラスを入力に取り、内部的には1次元配列として保持する事で動作を模倣している
    [Serializable]
    public class NdArray
    {
        public Real[] Data;
        public int[] Shape;

        public NdArray(Real[] data, int[] shape)
        {
            //コンストラクタはコピーを作成する
            this.Data = data.ToArray();
            this.Shape = shape.ToArray();
        }

        public NdArray(NdArray ndArray)
        {
            //コンストラクタはコピーを作成する
            this.Data = ndArray.Data.ToArray();
            this.Shape = ndArray.Shape.ToArray();
        }

        //ガワだけを作る
        protected NdArray() { }

        public int Rank
        {
            get { return this.Shape.Length; }
        }

        //データ部をコピーせずにインスタンスする
        public static NdArray Convert(Real[] data, int[] shape)
        {
            return new NdArray { Data = data, Shape = shape.ToArray() };
        }

        //データ部をコピーせずにインスタンスする
        public static NdArray Convert(Real[] data)
        {
            return new NdArray { Data = data, Shape = new[] { data.Length } };
        }

        public static NdArray ZerosLike(NdArray baseArray)
        {
            return new NdArray { Data = new Real[baseArray.Data.Length], Shape = baseArray.Shape.ToArray() };
        }

        public static NdArray OnesLike(NdArray baseArray)
        {
            Real[] resutlArray = new Real[baseArray.Data.Length];

            for (int i = 0; i < resutlArray.Length; i++)
            {
                resutlArray[i] = 1;
            }

            return new NdArray { Data = resutlArray, Shape = baseArray.Shape.ToArray() };
        }

        public static NdArray Zeros(params int[] shape)
        {
            return new NdArray { Data = new Real[ShapeToArrayLength(shape)], Shape = shape };
        }

        public static NdArray Ones(params int[] shape)
        {
            Real[] resutlArray = new Real[ShapeToArrayLength(shape)];

            for (int i = 0; i < resutlArray.Length; i++)
            {
                resutlArray[i] = 1;
            }

            return new NdArray { Data = resutlArray, Shape = shape };
        }

        protected static int ShapeToArrayLength(params int[] shapes)
        {
            int result = 1;

            foreach (int shape in shapes)
            {
                result *= shape;
            }

            return result;
        }

        public static NdArray FromArray(Array data)
        {
            Real[] resultData = new Real[data.Length];
            int[] resultShape;

            if (data.Rank == 1)
            {
                //型変換を兼ねる
                Array.Copy(data, resultData, data.Length);

                resultShape = new[] { data.Length };
            }
            else
            {
                //方の不一致をここで吸収
                if (data.GetType().GetElementType() != typeof(Real))
                {
                    Type arrayType = data.GetType().GetElementType();
                    //一次元の長さの配列を用意
                    Array array = Array.CreateInstance(arrayType, data.Length);
                    //一次元化して
                    Buffer.BlockCopy(data, 0, array, 0, Marshal.SizeOf(arrayType) * resultData.Length);

                    //型変換しつつコピー
                    Array.Copy(array, resultData, array.Length);
                }
                else
                {
                    Array.Copy(data, resultData, resultData.Length);
                }

                resultShape = new int[data.Rank];
                for (int i = 0; i < data.Rank; i++)
                {
                    resultShape[i] = data.GetLength(i);
                }
            }

            return new NdArray { Data = resultData, Shape = resultShape };
        }

        public void Fill(Real val)
        {
            for (int i = 0; i < this.Data.Length; i++)
            {
                this.Data[i] = val;
            }
        }

        //Numpyっぽく値を文字列に変換して出力する
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            int intMaxLength = 0;   //整数部の最大値
            int realMaxLength = 0;   //小数点以下の最大値
            bool isExponential = false; //指数表現にするか

            foreach (Real data in this.Data)
            {
                string[] divStr = data.ToString().Split('.');
                intMaxLength = Math.Max(intMaxLength, divStr[0].Length);
                if (divStr.Length > 1 && !isExponential)
                {
                    isExponential = divStr[1].Contains("E");
                }

                if (realMaxLength != 8 && divStr.Length == 2)
                {
                    realMaxLength = Math.Max(realMaxLength, divStr[1].Length);
                    if (realMaxLength > 8) realMaxLength = 8;
                }
            }

            //配列の約数を取得
            int[] commonDivisorList = new int[this.Shape.Length];

            //一個目は手動取得
            commonDivisorList[0] = this.Shape[this.Shape.Length - 1];
            for (int i = 1; i < this.Shape.Length; i++)
            {
                commonDivisorList[i] = commonDivisorList[i - 1] * this.Shape[this.Shape.Length - i - 1];
            }

            //先頭の括弧
            for (int i = 0; i < this.Shape.Length; i++)
            {
                sb.Append("[");
            }

            int closer = 0;
            for (int i = 0; i < this.Data.Length; i++)
            {
                string[] divStr;
                if (isExponential)
                {
                    divStr = string.Format("{0:0.00000000e+00}", this.Data[i]).Split('.');
                }
                else
                {
                    divStr = this.Data[i].ToString().Split('.');
                }

                //最大文字数でインデントを揃える
                for (int j = 0; j < intMaxLength - divStr[0].Length; j++)
                {
                    sb.Append(" ");
                }
                sb.Append(divStr[0]);
                if (realMaxLength != 0)
                {
                    sb.Append(".");
                }
                if (divStr.Length == 2)
                {
                    sb.Append(divStr[1].Length > 8 && !isExponential ? divStr[1].Substring(0, 8) : divStr[1]);
                    for (int j = 0; j < realMaxLength - divStr[1].Length; j++)
                    {
                        sb.Append(" ");
                    }
                }
                else
                {
                    for (int j = 0; j < realMaxLength; j++)
                    {
                        sb.Append(" ");
                    }
                }

                //約数を調査してピッタリなら括弧を出力
                if (i != this.Data.Length - 1)
                {
                    foreach (int commonDivisor in commonDivisorList)
                    {
                        if ((i + 1) % commonDivisor == 0)
                        {
                            sb.Append("]");
                            closer++;
                        }
                    }

                    sb.Append(" ");

                    if ((i + 1) % commonDivisorList[0] == 0)
                    {
                        //閉じ括弧分だけ改行を追加
                        for (int j = 0; j < closer; j++)
                        {
                            sb.Append("\n");
                        }
                        closer = 0;

                        //括弧前のインデント
                        foreach (int commonDivisor in commonDivisorList)
                        {
                            if ((i + 1) % commonDivisor != 0)
                            {
                                sb.Append(" ");
                            }
                        }
                    }

                    foreach (int commonDivisor in commonDivisorList)
                    {
                        if ((i + 1) % commonDivisor == 0)
                        {
                            sb.Append("[");
                        }
                    }
                }
            }

            //後端の括弧
            for (int i = 0; i < this.Shape.Length; i++)
            {
                sb.Append("]");
            }

            return sb.ToString();
        }

        //コピーを作成するメソッド
        public NdArray Clone()
        {
            return DeepCopyHelper.DeepCopy(this);
        }
    }
}
