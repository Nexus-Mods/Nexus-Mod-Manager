using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Nexus.Client.Util
{
	public class SortedObservableCollection<T> : ObservableCollection<T>
	{
		private bool m_booSuppressEvents = false;
		private bool m_booSorting = false;
		private IComparer<T> m_cmpComparer = null;

		#region Properties

		/// <summary>
		/// Sets the <see cref="IComparer"/> used to sort the items.
		/// </summary>
		/// <value>The <see cref="IComparer"/> used to sort the items.</value>
		public IComparer<T> Comparer
		{
			set
			{
				m_booSuppressEvents = true;
				m_cmpComparer = value;
				if (value != null)
					Sort();
				m_booSuppressEvents = false;
				OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public SortedObservableCollection()
			: this(null, null)
		{
		}

		/// <summary>
		/// A constructor that initializs the items of the list.
		/// </summary>
		/// <param name="p_enmItems">The items with which to initialize the list.</param>
		public SortedObservableCollection(IEnumerable<T> p_enmItems)
			: this(p_enmItems, null)
		{
		}

		/// <summary>
		/// A constructor that allows the specification of a custom comparer.
		/// </summary>
		/// <remarks>
		/// WARNING: The given <see cref="IComparer{T}"/> is only used when adding
		/// items to the list, and when calling <see cref="BinarySearch"/>. Calls to
		/// <see cref="Contains"/> and <see cref="IndexOf"/> do not use the given
		/// comparer.
		/// </remarks>
		/// <param name="p_cmpComparer">The comparer to use when determining if an item is already in the sorted list.</param>
		public SortedObservableCollection(IComparer<T> p_cmpComparer)
			: this(null, p_cmpComparer)
		{
		}

		/// <summary>
		/// A constructor that initializs the items of the list, and allows the specification of a custom comparer.
		/// </summary>
		/// <remarks>
		/// WARNING: The given <see cref="IComparer{T}"/> is only used when adding
		/// items to the list, and when calling <see cref="BinarySearch"/>. Calls to
		/// <see cref="Contains"/> and <see cref="IndexOf"/> do not use the given
		/// comparer.
		/// </remarks>
		/// <param name="p_enmItems">The items with which to initialize the list.</param>
		/// <param name="p_cmpComparer">The comparer to use when determining if an item is already in the sorted list.</param>
		public SortedObservableCollection(IEnumerable<T> p_enmItems, IComparer<T> p_cmpComparer)
			: base(p_enmItems)
		{
			if (!typeof(IComparable<T>).IsAssignableFrom(typeof(T)) && (p_cmpComparer == null))
				throw new ArgumentException("Type " + typeof(T).Name + " is not IComparable. Use SortedList(IComparer) to supply a comparer.");
			Comparer = p_cmpComparer;
		}

		/// <summary>
		/// The copy constructor.
		/// </summary>
		/// <remarks>
		/// This performs a shallow copy of the given list.
		/// </remarks>
		/// <param name="p_enmItems">The collection to copy.</param>
		public SortedObservableCollection(SortedObservableCollection<T> p_enmItems)
			: this(p_enmItems, p_enmItems.m_cmpComparer)
		{
		}

		#endregion

		private Int32 CompareItems(T p_tItem1, T p_tItem2)
		{
			if (m_cmpComparer != null)
				return m_cmpComparer.Compare(p_tItem1, p_tItem2);
			return ((IComparable<T>)p_tItem1).CompareTo(p_tItem2);
		}

		#region Sorting

		private void swap(Int32 p_intIndex1, Int32 p_intIndex2)
		{
			T tItem = this[p_intIndex1];
			base.SetItem(p_intIndex1, this[p_intIndex2]);
			base.SetItem(p_intIndex2, tItem);
		}

		private Int32 partition(Int32 left, Int32 right, Int32 pivotIndex)
		{
			T pivotValue = this[pivotIndex];
			swap(pivotIndex, right);
			Int32 storeIndex = left;
			for (Int32 i = left; i < right; i++)
			{
				if (CompareItems(this[i], pivotValue) < 1)
				{
					swap(i, storeIndex);
					storeIndex++;
				}
			}
			swap(storeIndex, right);
			return storeIndex;
		}

		private void quicksort(Int32 left, Int32 right)
		{
			if (right > left)
			{
				Int32 pivotIndex = (right + left) / 2;
				Int32 pivotNewIndex = partition(left, right, pivotIndex);
				quicksort(left, pivotNewIndex - 1);
				quicksort(pivotNewIndex + 1, right);
			}
		}

		protected void Sort()
		{
			m_booSuppressEvents = true;
			m_booSorting = true;
			quicksort(0, Count - 1);
			m_booSorting = false;
			m_booSuppressEvents = false;
		}

		#endregion

		#region Searching

		public Int32 BinarySearch(T p_tItem, Int32 p_intLeft, Int32 p_intRight)
		{
			if (p_intRight < p_intLeft)
			{
				return ~p_intLeft;
			}
			Int32 intMiddleIndex = (p_intRight + p_intLeft) / 2;
			Int32 intResult = CompareItems(p_tItem, this[intMiddleIndex]);
			if (intResult == 0)
				return intMiddleIndex;
			if (intResult < 0)
				return BinarySearch(p_tItem, p_intLeft, intMiddleIndex - 1);
			return BinarySearch(p_tItem, intMiddleIndex + 1, p_intRight);
		}

		public Int32 BinarySearch(T p_tItem)
		{
			return BinarySearch(p_tItem, 0, Count - 1);
		}

		#endregion

		/// <summary>
		/// Inserts the given item at the given index.
		/// </summary>
		/// <remarks>
		/// Random inserts don't make sense in an ordered list, so inserts are only
		/// allowed if they're as a result of an add operation.
		/// </remarks>
		/// <param name="index">The index at which to insert the item.</param>
		/// <param name="item">The item to insert.</param>
		/// <exception cref="InvalidOperationException">Thrown if the item isn't being inserted
		/// as a result of an add operation.</exception>
		protected override void InsertItem(int index, T item)
		{
			if (index < Count)
				throw new InvalidOperationException("The method or operation cannot be performed on an Ordered List.");
			Int32 intIndex = BinarySearch(item);
			if (intIndex < 0)
				intIndex = ~intIndex;
			base.InsertItem(intIndex, item);
		}

		protected override void MoveItem(int oldIndex, int newIndex)
		{
			throw new InvalidOperationException("The method or operation cannot be performed on an Ordered List.");
		}

		protected override void RemoveItem(int index)
		{
			base.RemoveItem(index);
		}

		/// <summary>
		/// Sets the specified index to the given item.
		/// </summary>
		/// <remarks>
		/// This operation does not make sense in a set, so this method
		/// always throws an <see cref="InvalidOperationException"/>,
		/// unless it is being called as the result of an internal sorting call.
		/// </remarks>
		/// <param name="index">The index at which to set the item.</param>
		/// <param name="item">The item to set.</param>
		/// <exception cref="InvalidOperationException">Thrown always,
		/// unless the method is being called as the result of an internal sorting call..</exception>
		protected override void SetItem(int index, T item)
		{
			if (m_booSorting)
				base.SetItem(index, item);
			else
				throw new InvalidOperationException("The method or operation cannot be performed on an Ordered List.");
		}

		protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
		{
			if (!m_booSuppressEvents)
				base.OnCollectionChanged(e);
		}

		protected override void OnPropertyChanged(PropertyChangedEventArgs e)
		{
			if (!m_booSuppressEvents)
				base.OnPropertyChanged(e);
		}
	}
}
