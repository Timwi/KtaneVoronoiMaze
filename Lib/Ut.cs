using System;
using System.Collections.Generic;

namespace RT.Util.ExtensionMethods
{
    static class Ut
    {
        /// <summary>
        ///     Transforms every element of an input collection using two selector functions and returns a collection
        ///     containing all the results.</summary>
        /// <typeparam name="TSource">
        ///     Type of the elements in the source collection.</typeparam>
        /// <typeparam name="TResult">
        ///     Type of the results of the selector functions.</typeparam>
        /// <param name="source">
        ///     Input collection to transform.</param>
        /// <param name="selector1">
        ///     First selector function.</param>
        /// <param name="selector2">
        ///     Second selector function.</param>
        /// <returns>
        ///     A collection containing the transformed elements from both selectors, thus containing twice as many elements
        ///     as the original collection.</returns>
        public static IEnumerable<TResult> SelectTwo<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector1, Func<TSource, TResult> selector2)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector1 == null)
                throw new ArgumentNullException(nameof(selector1));
            if (selector2 == null)
                throw new ArgumentNullException(nameof(selector2));

            IEnumerable<TResult> selectTwoIterator()
            {
                foreach (var elem in source)
                {
                    yield return selector1(elem);
                    yield return selector2(elem);
                }
            }
            return selectTwoIterator();
        }

        /// <summary>
        ///     Returns an enumeration of tuples containing all consecutive pairs of the elements.</summary>
        /// <param name="source">
        ///     The input enumerable.</param>
        /// <param name="closed">
        ///     If true, an additional pair containing the last and first element is included. For example, if the source
        ///     collection contains { 1, 2, 3, 4 } then the enumeration contains { (1, 2), (2, 3), (3, 4) } if <paramref
        ///     name="closed"/> is false, and { (1, 2), (2, 3), (3, 4), (4, 1) } if <paramref name="closed"/> is true.</param>
        public static IEnumerable<(T, T)> ConsecutivePairs<T>(this IEnumerable<T> source, bool closed) => SelectConsecutivePairs(source, closed, (i1, i2) => (i1, i2));

        /// <summary>
        ///     Enumerates all consecutive pairs of the elements.</summary>
        /// <param name="source">
        ///     The input enumerable.</param>
        /// <param name="closed">
        ///     If true, an additional pair containing the last and first element is included. For example, if the source
        ///     collection contains { 1, 2, 3, 4 } then the enumeration contains { (1, 2), (2, 3), (3, 4) } if <paramref
        ///     name="closed"/> is <c>false</c>, and { (1, 2), (2, 3), (3, 4), (4, 1) } if <paramref name="closed"/> is
        ///     <c>true</c>.</param>
        /// <param name="selector">
        ///     The selector function to run each consecutive pair through.</param>
        public static IEnumerable<TResult> SelectConsecutivePairs<T, TResult>(this IEnumerable<T> source, bool closed, Func<T, T, TResult> selector)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            IEnumerable<TResult> selectConsecutivePairsIterator()
            {
                using (var enumer = source.GetEnumerator())
                {
                    bool any = enumer.MoveNext();
                    if (!any)
                        yield break;
                    T first = enumer.Current;
                    T last = enumer.Current;
                    while (enumer.MoveNext())
                    {
                        yield return selector(last, enumer.Current);
                        last = enumer.Current;
                    }
                    if (closed)
                        yield return selector(last, first);
                }
            }
            return selectConsecutivePairsIterator();
        }

        /// <summary>
        ///     Returns the parameters as a new array.</summary>
        /// <remarks>
        ///     Useful to circumvent Visual Studio’s bug where multi-line literal arrays are not auto-formatted.</remarks>
        public static T[] NewArray<T>(params T[] parameters) { return parameters; }
    }
}
