﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

/*============================================================
**
**
**
** Purpose: Convenient wrapper for an array, an offset, and
**          a count.  Ideally used in streams & collections.
**          Net Classes will consume an array of these.
**
**
===========================================================*/

using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace System
{
	// Note: users should make sure they copy the fields out of an ArraySegment onto their stack
	// then validate that the fields describe valid bounds within the array.  This must be done
	// because assignments to value types are not atomic, and also because one thread reading
	// three fields from an ArraySegment may not see the same ArraySegment from one call to another
	// (ie, users could assign a new value to the old location).
	[Serializable]
	public readonly struct RecyclableArraySegment<T> : IList<T>, IReadOnlyList<T>, IDisposable
	{
		// Do not replace the array allocation with Array.Empty. We don't want to have the overhead of
		// instantiating another generic type in addition to ArraySegment<T> for new type parameters.
#pragma warning disable CA1825
		public static RecyclableArraySegment<T> Empty { get; } = new RecyclableArraySegment<T>(new T[0]);
#pragma warning restore CA1825

		private readonly T[] _array; // Do not rename (binary serialization)
		private readonly int _offset; // Do not rename (binary serialization)
		private readonly int _count; // Do not rename (binary serialization)

		public RecyclableArraySegment(T[] array)
		{
			if(array == null)
				throw new ArgumentNullException(nameof(array));

			_array = array;
			_offset = 0;
			_count = array.Length;
		}

		public RecyclableArraySegment(T[] array, int offset, int count)
		{
			// Validate arguments, check is minimal instructions with reduced branching for inlinable fast-path
			// Negative values discovered though conversion to high values when converted to unsigned
			// Failure should be rare and location determination and message is delegated to failure functions
			if(array == null || (uint)offset > (uint)array.Length || (uint)count > (uint)(array.Length - offset))
				throw new InvalidOperationException($"{array} {offset} {count}");

			_array = array;
			_offset = offset;
			_count = count;
		}

		public T[] Array => _array;

		public int Offset => _offset;

		public int Count => _count;

		public T this[int index]
		{
			get
			{
				if((uint)index >= (uint)_count)
				{
					throw new ArgumentOutOfRangeException();
				}

				return _array[_offset + index];
			}
			set
			{
				if((uint)index >= (uint)_count)
				{
					throw new IndexOutOfRangeException();
				}

				_array[_offset + index] = value;
			}
		}

		public Enumerator GetEnumerator()
		{
			ThrowInvalidOperationIfDefault();
			return new Enumerator(this);
		}

		public override int GetHashCode() =>
			_array is null ? 0 : _array.GetHashCode();

		public void CopyTo(T[] destination) => CopyTo(destination, 0);

		public void CopyTo(T[] destination, int destinationIndex)
		{
			ThrowInvalidOperationIfDefault();
			System.Array.Copy(_array, _offset, destination, destinationIndex, _count);
		}

		public void CopyTo(RecyclableArraySegment<T> destination)
		{
			ThrowInvalidOperationIfDefault();
			destination.ThrowInvalidOperationIfDefault();

			if(_count > destination._count)
			{
				throw new ArgumentException();
			}

			System.Array.Copy(_array, _offset, destination._array, destination._offset, _count);
		}

		public override bool Equals(object obj) =>
			obj is RecyclableArraySegment<T> && Equals((RecyclableArraySegment<T>)obj);

		public bool Equals(RecyclableArraySegment<T> obj) =>
			obj._array == _array && obj._offset == _offset && obj._count == _count;

		public RecyclableArraySegment<T> Slice(int index)
		{
			ThrowInvalidOperationIfDefault();

			if((uint)index > (uint)_count)
			{
				throw new IndexOutOfRangeException();
			}

			return new RecyclableArraySegment<T>(_array, _offset + index, _count - index);
		}

		public ArraySegment<T> Slice(int index, int count)
		{
			ThrowInvalidOperationIfDefault();

			if((uint)index > (uint)_count || (uint)count > (uint)(_count - index))
			{
				throw new ArgumentOutOfRangeException();
			}

			return new ArraySegment<T>(_array, _offset + index, count);
		}

		public T[] ToArray()
		{
			ThrowInvalidOperationIfDefault();

			if(_count == 0)
			{
				return Empty._array;
			}

			var array = new T[_count];
			System.Array.Copy(_array, _offset, array, 0, _count);
			return array;
		}

		public static bool operator ==(RecyclableArraySegment<T> a, RecyclableArraySegment<T> b) => a.Equals(b);

		public static bool operator !=(RecyclableArraySegment<T> a, RecyclableArraySegment<T> b) => !(a == b);

		public static implicit operator RecyclableArraySegment<T>(T[] array) => array != null ? new RecyclableArraySegment<T>(array) : default;

		#region IList<T>
		T IList<T>.this[int index]
		{
			get
			{
				ThrowInvalidOperationIfDefault();
				if (index < 0 || index >= _count)
					throw new IndexOutOfRangeException();

				return _array[_offset + index];
			}

			set
			{
				ThrowInvalidOperationIfDefault();
				if(index < 0 || index >= _count)
					throw new IndexOutOfRangeException();

				_array[_offset + index] = value;
			}
		}

		int IList<T>.IndexOf(T item)
		{
			ThrowInvalidOperationIfDefault();

			int index = System.Array.IndexOf<T>(_array, item, _offset, _count);

			Debug.Assert(index == -1 ||
							(index >= _offset && index < _offset + _count));

			return index >= 0 ? index - _offset : -1;
		}

		void IList<T>.Insert(int index, T item) => throw new NotSupportedException();

		void IList<T>.RemoveAt(int index) => throw new NotSupportedException();
		#endregion

		#region IReadOnlyList<T>
		T IReadOnlyList<T>.this[int index]
		{
			get
			{
				ThrowInvalidOperationIfDefault();
				if(index < 0 || index >= _count)
					throw new IndexOutOfRangeException();

				return _array[_offset + index];
			}
		}
		#endregion IReadOnlyList<T>

		#region ICollection<T>
		bool ICollection<T>.IsReadOnly =>
			// the indexer setter does not throw an exception although IsReadOnly is true.
			// This is to match the behavior of arrays.
			true;

		void ICollection<T>.Add(T item) => throw new NotSupportedException();

		void ICollection<T>.Clear() => throw new NotSupportedException();

		bool ICollection<T>.Contains(T item)
		{
			ThrowInvalidOperationIfDefault();

			int index = System.Array.IndexOf<T>(_array, item, _offset, _count);

			Debug.Assert(index == -1 ||
							(index >= _offset && index < _offset + _count));

			return index >= 0;
		}

		bool ICollection<T>.Remove(T item)
		{
			throw new NotSupportedException();
			return default;
		}
		#endregion

		#region IEnumerable<T>

		IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
		#endregion

		#region IEnumerable

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		#endregion

		private void ThrowInvalidOperationIfDefault()
		{
			if(_array == null)
			{
				throw new InvalidOperationException("Null array");
			}
		}

		public struct Enumerator : IEnumerator<T>
		{
			private readonly T[] _array;
			private readonly int _start;
			private readonly int _end; // cache Offset + Count, since it's a little slow
			private int _current;

			internal Enumerator(RecyclableArraySegment<T> arraySegment)
			{
				Debug.Assert(arraySegment.Array != null);
				Debug.Assert(arraySegment.Offset >= 0);
				Debug.Assert(arraySegment.Count >= 0);
				Debug.Assert(arraySegment.Offset + arraySegment.Count <= arraySegment.Array.Length);

				_array = arraySegment.Array;
				_start = arraySegment.Offset;
				_end = arraySegment.Offset + arraySegment.Count;
				_current = arraySegment.Offset - 1;
			}

			public bool MoveNext()
			{
				if(_current < _end)
				{
					_current++;
					return _current < _end;
				}
				return false;
			}

			public T Current
			{
				get
				{
					if(_current < _start)
						throw new InvalidOperationException();
					if(_current >= _end)
						throw new InvalidOperationException();
					return _array[_current];
				}
			}

			object IEnumerator.Current => Current;

			void IEnumerator.Reset()
			{
				_current = _start - 1;
			}

			public void Dispose()
			{
			}
		}

		public void Dispose()
		{
			if(_array != null)
				ArrayPool<T>.Shared.Return(this._array);
		}
	}
}