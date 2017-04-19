﻿using KelpNet.Common;

namespace KelpNetTester.Benchmarker
{
    class BenchDataMaker
    {
        public static Real[] GetDoubleArray(int length)
        {
            Real[] result = new Real[length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (Real)Mother.Dice.NextDouble();
            }

            return result;
        }
    }
}
