﻿using System;
using System.Collections.Generic;
using MahalanobisTaguchiSystem.Interfaces;

namespace MahalanobisTaguchiSystem
{
    public class Mts<T> where T : struct,
        IComparable,
        IComparable<T>,
        IConvertible,
        IEquatable<T>,
        IFormattable
    {
        private const double Tolerance = 0.000001;
        private readonly IMtsMathProvider<T> _provider;
        private readonly IMtsFactory<T> _factory;

        // ReSharper disable once MemberCanBeProtected.Global
        public Mts(IMtsMathProvider<T> provider, IMtsFactory<T> factory)
        {
            _provider = provider;
            _factory = factory;
        }
        
        public double GetMahalanobisDistance(ISpace<T> space, ISample<T> sample)
        {
            var z = CalculateZ(space, sample);
            var inverseC = GetInverseCorrelationSpace(space);
            var transposeZ = _factory.CreateSingleVariableSpaceFromSample(z);

            var step1 = _provider.Multiply(z, inverseC);
            var step2 = _provider.Multiply(step1, transposeZ);

            return 2 * step2[0] / space.Samples;
        }

        public IList<bool> FindUsefulVariables(ISpace<T> space, ISample<T> sample)
        {
            var sampleSpace = _factory.CreateSingleSampleSpaceFromSample(sample);

            return FindUsefulVariables(space, sampleSpace);
        }
        
        public IList<bool> FindUsefulVariables(ISpace<T> space, ISpace<T> samples)
        {
            var variables = samples.Variables;

            var timesUsed = new int[variables];
            var timesNotUsed = new int[variables];
            var signalToNoiseWhenUsed = new double[variables];
            var signalToNoiseWhenNotUsed = new double[variables];

            var oa = GetOrthogonalSpace(variables);

            for (var run = 0; run < oa.Samples; ++run)
            {
                var tempSpace = _factory.CreateSpaceFromArray(space.Storage);
                var tempSample = _factory.CreateSpaceFromArray(samples.Storage);

                for (var v = variables - 1; v >= 0; --v)
                {
                    if (Math.Abs(oa[run, v] - 1) < Tolerance) continue;
                    tempSpace.RemoveVariable(v);
                    tempSample.RemoveVariable(v);
                }

                var signalToNoise = 0d;
                for (var s = 0; s < tempSample.Samples; ++s)
                {
                    var current = tempSample.GetSample(s);
                    signalToNoise += 1 / GetMahalanobisDistance(tempSpace, current);
                }

                signalToNoise = -10 * Math.Log(signalToNoise / samples.Samples);

                for (var column = 0; column < variables; ++column)
                {
                    if (Math.Abs(oa[run, column] - 1) < Tolerance)
                    {
                        timesUsed[column]++;
                        signalToNoiseWhenUsed[column] += signalToNoise;
                    }
                    else
                    {
                        timesNotUsed[column]++;
                        signalToNoiseWhenNotUsed[column] += signalToNoise;
                    }
                }
            }
            
            var result = new List<bool>();
            for (var i = 0; i < variables; ++i)
            {
                result.Add(
                    signalToNoiseWhenUsed[i] / timesUsed[i] -
                    signalToNoiseWhenNotUsed[i] / timesNotUsed[i] >= 0);
            }

            return result;
        }

        protected ISpace<T> GetOrthogonalSpace(int varCount)
        {
            if (varCount >= 8 && varCount < 12) // L12 OAs are weird
                return _factory.GenerateL12();

            var runs = CeilingToPowerOfTwo(varCount + 1);

            var oa = new T[runs, runs];

            // Power-of-two columns (1, 2, 4, etc.) start at 0 and toggle between 1 and 0 every runs/(2 * column number) rows.
            // Each other column is a binary addition of the power-of-two columns that add up to it ([n,3] = [n,1] + [n,2]).
            for (var column = 1; column < runs; column++)
            {
                if (IsPowerOfTwo(column))
                {
                    var value = 1;

                    for (var row = 0; row < runs; row++)
                    {
                        var temp = runs / (2 * column);
                        if (temp != 0 && row % temp == 0) value ^= 1;
                        oa[row, column] = _provider.CastToT(value);
                    }
                }
                else
                {
                    for (var row = 0; row < runs; row++)
                    {
                        for (var digit = 0; GetTwoToTheNthPower(digit) < column; digit++) // for each power of two
                        {
                            var digitColumn = GetTwoToTheNthPower(digit);
                            if ((column & digitColumn) == digitColumn)
                            {
                                oa[row, column] = _provider.Modulo(_provider.Add(oa[row, column], oa[row, digitColumn]), _provider.CastToT(2));
                            }
                        }
                    }
                }
            }
            for (var i = 0; i < runs; ++i)
                for (var j = 0; j < runs; ++j)
                    oa[i, j] = _provider.Add(oa[i, j], _provider.CastToT(1));

            var space = _factory.CreateSpaceFromArray(oa);
            space.RemoveVariable(0);

            return space;
        }

        protected ISample<T> CalculateZ(ISpace<T> space, ISample<T> sample)
        {
            var means = GetVariableMeans(space);
            var stdDevs = GetVariableStandardDeviations(space);

            var z = new T[sample.Variables];
            for (var i = 0; i < sample.Variables; ++i)
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (stdDevs[i] != 0)
                    z[i] = _provider.CastToT((sample[i] - means[i]) / stdDevs[i]);
            }

            return _factory.CreateSampleFromArray(z);
        }

        private double[] GetVariableMeans(ISpace<T> space)
        {
            var means = new double[space.Variables];
            for (var i = 0; i < space.Variables; ++i)
                means[i] = _provider.GetMeanOfValues(space.GetVariableValues(i));

            return means;
        }

        private double[] GetVariableStandardDeviations(ISpace<T> space)
        {
            var stdDevs = new double[space.Variables];
            var means = GetVariableMeans(space);
            for (var i = 0; i < space.Samples; ++i)
                for (var j = 0; j < space.Variables; ++j)
                    stdDevs[j] += (space[i, j] - means[j]) * (space[i, j] - means[j]) / space.Samples;

            for (var i = 0; i < stdDevs.Length; ++i)
                stdDevs[i] = Math.Sqrt(stdDevs[i]);

            return stdDevs;
        }

        private ISpace<T> GetInverseCorrelationSpace(ISpace<T> space)
        {
            var temp = new T[space.Variables, space.Variables];
            for (var i = 0; i < space.Variables; i++)
            {
                for (var j = 0; j < space.Variables; j++)
                {
                    temp[i, j] = _provider.GetCorrelation(space.GetVariableValues(i), space.GetVariableValues(j));
                }
            }
            var inverseSpace = _provider.GetInverseSpace(_factory.CreateSpaceFromArray(temp));

            return inverseSpace;
        }

        protected static bool IsPowerOfTwo(int n)
        {
            return (n & (n - 1)) == 0 && n != 0;
        }

        protected static int CeilingToPowerOfTwo(int n)
        {
            if (n <= 0)
                return 1;

            n--;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            n++;

            return n;
        }

        protected static int GetTwoToTheNthPower(int n)
        {
            return 1 << n;
        }
    }
}
