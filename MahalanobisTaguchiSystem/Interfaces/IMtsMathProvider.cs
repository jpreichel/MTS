﻿using System;

namespace MahalanobisTaguchiSystem.Interfaces
{
    // T : int, double, long, etc.
    public interface IMtsMathProvider<T> where T : struct,
          IComparable,
          IComparable<T>,
          IConvertible,
          IEquatable<T>,
          IFormattable
    {
        ISample<T> Multiply(ISample<T> sample, ISpace<T> space);
        ISpace<T> GetInverseSpace(ISpace<T> space);
        T GetCorrelation(T[] a, T[] b);
        double GetMeanOfValues(T[] values);
        T CastToT(int value);
        T CastToT(double value);
        T Add(T a, T b);
        T Modulo(T divedend, T divisor);
    }
}
