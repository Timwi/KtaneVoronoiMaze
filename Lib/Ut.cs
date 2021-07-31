using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RT.Util.ExtensionMethods
{
    static class Ut
    {
        /// <summary>
        ///     Turns all elements in the enumerable to strings and joins them using the specified <paramref
        ///     name="separator"/> and the specified <paramref name="prefix"/> and <paramref name="suffix"/> for each string.</summary>
        /// <param name="values">
        ///     The sequence of elements to join into a string.</param>
        /// <param name="separator">
        ///     Optionally, a separator to insert between each element and the next.</param>
        /// <param name="prefix">
        ///     Optionally, a string to insert in front of each element.</param>
        /// <param name="suffix">
        ///     Optionally, a string to insert after each element.</param>
        /// <param name="lastSeparator">
        ///     Optionally, a separator to use between the second-to-last and the last element.</param>
        /// <example>
        ///     <code>
        ///         // Returns "[Paris], [London], [Tokyo]"
        ///         (new[] { "Paris", "London", "Tokyo" }).JoinString(", ", "[", "]")
        ///         
        ///         // Returns "[Paris], [London] and [Tokyo]"
        ///         (new[] { "Paris", "London", "Tokyo" }).JoinString(", ", "[", "]", " and ");</code></example>
        public static string JoinString<T>(this IEnumerable<T> values, string separator = null, string prefix = null, string suffix = null, string lastSeparator = null)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (lastSeparator == null)
                lastSeparator = separator;

            using (var enumerator = values.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                    return "";

                // Optimise the case where there is only one element
                var one = enumerator.Current;
                if (!enumerator.MoveNext())
                    return prefix + one + suffix;

                // Optimise the case where there are only two elements
                var two = enumerator.Current;
                if (!enumerator.MoveNext())
                {
                    // Optimise the (common) case where there is no prefix/suffix; this prevents an array allocation when calling string.Concat()
                    if (prefix == null && suffix == null)
                        return one + lastSeparator + two;
                    return prefix + one + suffix + lastSeparator + prefix + two + suffix;
                }

                StringBuilder sb = new StringBuilder()
                    .Append(prefix).Append(one).Append(suffix).Append(separator)
                    .Append(prefix).Append(two).Append(suffix);
                var prev = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    sb.Append(separator).Append(prefix).Append(prev).Append(suffix);
                    prev = enumerator.Current;
                }
                sb.Append(lastSeparator).Append(prefix).Append(prev).Append(suffix);
                return sb.ToString();
            }
        }

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

        /// <summary>
        ///     Returns the index of the first element in this <paramref name="source"/> satisfying the specified <paramref
        ///     name="predicate"/>. If no such elements are found, returns <c>-1</c>.</summary>
        public static int IndexOf<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            int index = 0;
            foreach (var v in source)
            {
                if (predicate(v))
                    return index;
                index++;
            }
            return -1;
        }

        /// <summary>
        ///     Returns a collection of integers containing the indexes at which the elements of the source collection match
        ///     the given predicate.</summary>
        /// <typeparam name="T">
        ///     The type of elements in the collection.</typeparam>
        /// <param name="source">
        ///     The source collection whose elements are tested using <paramref name="predicate"/>.</param>
        /// <param name="predicate">
        ///     The predicate against which the elements of <paramref name="source"/> are tested.</param>
        /// <returns>
        ///     A collection containing the zero-based indexes of all the matching elements, in increasing order.</returns>
        public static IEnumerable<int> SelectIndexWhere<T>(this IEnumerable<T> source, Predicate<T> predicate)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            IEnumerable<int> selectIndexWhereIterator()
            {
                int i = 0;
                using (var e = source.GetEnumerator())
                {
                    while (e.MoveNext())
                    {
                        if (predicate(e.Current))
                            yield return i;
                        i++;
                    }
                }
            }
            return selectIndexWhereIterator();
        }

        /// <summary>
        ///     Returns the minimum resulting value in a sequence, or a default value if the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the minimum value of.</param>
        /// <param name="default">
        ///     A default value to return in case the sequence is empty.</param>
        /// <returns>
        ///     The minimum value in the sequence, or the specified default value if the sequence is empty.</returns>
        public static TSource MinOrDefault<TSource>(this IEnumerable<TSource> source, TSource @default = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var (result, found) = minMax(source, min: true);
            return found ? result : @default;
        }

        /// <summary>
        ///     Invokes a selector on each element of a collection and returns the minimum resulting value, or a default value
        ///     if the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <typeparam name="TResult">
        ///     The type of the value returned by <paramref name="selector"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the minimum value of.</param>
        /// <param name="selector">
        ///     A transform function to apply to each element.</param>
        /// <param name="default">
        ///     A default value to return in case the sequence is empty.</param>
        /// <returns>
        ///     The minimum value in the sequence, or the specified default value if the sequence is empty.</returns>
        public static TResult MinOrDefault<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector, TResult @default = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            var (result, found) = minMax(source.Select(selector), min: true);
            return found ? result : @default;
        }

        /// <summary>
        ///     Returns the maximum resulting value in a sequence, or a default value if the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the maximum value of.</param>
        /// <param name="default">
        ///     A default value to return in case the sequence is empty.</param>
        /// <returns>
        ///     The maximum value in the sequence, or the specified default value if the sequence is empty.</returns>
        public static TSource MaxOrDefault<TSource>(this IEnumerable<TSource> source, TSource @default = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var (result, found) = minMax(source, min: false);
            return found ? result : @default;
        }

        /// <summary>
        ///     Invokes a selector on each element of a collection and returns the maximum resulting value, or a default value
        ///     if the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <typeparam name="TResult">
        ///     The type of the value returned by <paramref name="selector"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the maximum value of.</param>
        /// <param name="selector">
        ///     A transform function to apply to each element.</param>
        /// <param name="default">
        ///     A default value to return in case the sequence is empty.</param>
        /// <returns>
        ///     The maximum value in the sequence, or the specified default value if the sequence is empty.</returns>
        public static TResult MaxOrDefault<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector, TResult @default = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            var (result, found) = minMax(source.Select(selector), min: false);
            return found ? result : @default;
        }

        /// <summary>
        ///     Returns the minimum resulting value in a sequence, or <c>null</c> if the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the minimum value of.</param>
        /// <returns>
        ///     The minimum value in the sequence, or <c>null</c> if the sequence is empty.</returns>
        public static TSource? MinOrNull<TSource>(this IEnumerable<TSource> source) where TSource : struct
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var (result, found) = minMax(source, min: true);
            return found ? result : (TSource?) null;
        }

        /// <summary>
        ///     Invokes a selector on each element of a collection and returns the minimum resulting value, or <c>null</c> if
        ///     the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <typeparam name="TResult">
        ///     The type of the value returned by <paramref name="selector"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the minimum value of.</param>
        /// <param name="selector">
        ///     A transform function to apply to each element.</param>
        /// <returns>
        ///     The minimum value in the sequence, or <c>null</c> if the sequence is empty.</returns>
        public static TResult? MinOrNull<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) where TResult : struct
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            var (result, found) = minMax(source.Select(selector), min: true);
            return found ? result : (TResult?) null;
        }

        /// <summary>
        ///     Returns the maximum resulting value in a sequence, or <c>null</c> if the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the maximum value of.</param>
        /// <returns>
        ///     The maximum value in the sequence, or <c>null</c> if the sequence is empty.</returns>
        public static TSource? MaxOrNull<TSource>(this IEnumerable<TSource> source) where TSource : struct
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var (result, found) = minMax(source, min: false);
            return found ? result : (TSource?) null;
        }

        /// <summary>
        ///     Invokes a selector on each element of a collection and returns the maximum resulting value, or <c>null</c> if
        ///     the sequence is empty.</summary>
        /// <typeparam name="TSource">
        ///     The type of the elements of <paramref name="source"/>.</typeparam>
        /// <typeparam name="TResult">
        ///     The type of the value returned by <paramref name="selector"/>.</typeparam>
        /// <param name="source">
        ///     A sequence of values to determine the maximum value of.</param>
        /// <param name="selector">
        ///     A transform function to apply to each element.</param>
        /// <returns>
        ///     The maximum value in the sequence, or <c>null</c> if the sequence is empty.</returns>
        public static TResult? MaxOrNull<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) where TResult : struct
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            var (result, found) = minMax(source.Select(selector), min: false);
            return found ? result : (TResult?) null;
        }

        private static (T result, bool found) minMax<T>(IEnumerable<T> source, bool min)
        {
            var cmp = Comparer<T>.Default;
            var curBest = default(T);
            var haveBest = false;
            foreach (var elem in source)
            {
                if (!haveBest || (min ? cmp.Compare(elem, curBest) < 0 : cmp.Compare(elem, curBest) > 0))
                {
                    curBest = elem;
                    haveBest = true;
                }
            }
            return (curBest, haveBest);
        }

        /// <summary>
        ///     Returns the first element from the input sequence for which the value selector returns the smallest value.</summary>
        /// <exception cref="InvalidOperationException">
        ///     The input collection is empty.</exception>
        public static T MinElement<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector) where TValue : IComparable<TValue> =>
            minMaxElement(source, valueSelector, min: true, doThrow: true).Value.minMaxElem;

        /// <summary>
        ///     Returns the first element from the input sequence for which the value selector returns the smallest value, or
        ///     a default value if the collection is empty.</summary>
        public static T MinElementOrDefault<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector, T defaultValue = default) where TValue : IComparable<TValue>
        {
            var tup = minMaxElement(source, valueSelector, min: true, doThrow: false);
            return tup == null ? defaultValue : tup.Value.minMaxElem;
        }

        /// <summary>
        ///     Returns the first element from the input sequence for which the value selector returns the largest value.</summary>
        /// <exception cref="InvalidOperationException">
        ///     The input collection is empty.</exception>
        public static T MaxElement<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector) where TValue : IComparable<TValue> =>
            minMaxElement(source, valueSelector, min: false, doThrow: true).Value.minMaxElem;

        /// <summary>
        ///     Returns the first element from the input sequence for which the value selector returns the largest value, or a
        ///     default value if the collection is empty.</summary>
        public static T MaxElementOrDefault<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector, T defaultValue = default(T)) where TValue : IComparable<TValue>
        {
            var tup = minMaxElement(source, valueSelector, min: false, doThrow: false);
            return tup == null ? defaultValue : tup.Value.minMaxElem;
        }

        /// <summary>
        ///     Returns the index of the first element from the input sequence for which the value selector returns the
        ///     smallest value.</summary>
        /// <exception cref="InvalidOperationException">
        ///     The input collection is empty.</exception>
        public static int MinIndex<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector) where TValue : IComparable<TValue> =>
            minMaxElement(source, valueSelector, min: true, doThrow: true).Value.minMaxIndex;

        /// <summary>
        ///     Returns the index of the first element from the input sequence for which the value selector returns the
        ///     smallest value, or <c>null</c> if the collection is empty.</summary>
        public static int? MinIndexOrNull<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector) where TValue : IComparable<TValue> =>
            minMaxElement(source, valueSelector, min: true, doThrow: false)?.minMaxIndex;

        /// <summary>
        ///     Returns the index of the first element from the input sequence for which the value selector returns the
        ///     largest value.</summary>
        /// <exception cref="InvalidOperationException">
        ///     The input collection is empty.</exception>
        public static int MaxIndex<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector) where TValue : IComparable<TValue> =>
            minMaxElement(source, valueSelector, min: false, doThrow: true).Value.minMaxIndex;

        /// <summary>
        ///     Returns the index of the first element from the input sequence for which the value selector returns the
        ///     largest value, or a default value if the collection is empty.</summary>
        public static int? MaxIndexOrNull<T, TValue>(this IEnumerable<T> source, Func<T, TValue> valueSelector) where TValue : IComparable<TValue> =>
            minMaxElement(source, valueSelector, min: false, doThrow: false)?.minMaxIndex;

        private static (int minMaxIndex, T minMaxElem)? minMaxElement<T, TValue>(IEnumerable<T> source, Func<T, TValue> valueSelector, bool min, bool doThrow) where TValue : IComparable<TValue>
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (valueSelector == null)
                throw new ArgumentNullException(nameof(valueSelector));

            using (var enumerator = source.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                {
                    if (doThrow)
                        throw new InvalidOperationException("source contains no elements.");
                    return null;
                }
                var minMaxElem = enumerator.Current;
                var minMaxValue = valueSelector(minMaxElem);
                var minMaxIndex = 0;
                var curIndex = 0;
                while (enumerator.MoveNext())
                {
                    curIndex++;
                    var value = valueSelector(enumerator.Current);
                    if (min ? (value.CompareTo(minMaxValue) < 0) : (value.CompareTo(minMaxValue) > 0))
                    {
                        minMaxValue = value;
                        minMaxElem = enumerator.Current;
                        minMaxIndex = curIndex;
                    }
                }
                return (minMaxIndex, minMaxElem);
            }
        }
    }
}
