using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Nexus.Client.Util;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Event arguments describing an error that has changed for a specific property.
	/// </summary>
	public class ErrorEventArguments : EventArgs
	{
		#region Properties
		
		/// <summary>
		/// Gets the property for which the error has changed.
		/// </summary>
		/// <value>The property for which the error has changed.</value>
		public string Property { get; private set; }

		/// <summary>
		/// Gets the error.
		/// </summary>
		/// <value>The error.</value>
		public string Error { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strProperty">The property for which the error has changed.</param>
		/// <param name="p_strError">The error.</param>
		public ErrorEventArguments(string p_strProperty, string p_strError)
		{
			Property = p_strProperty;
			Error = p_strError;
		}

		#endregion
	}

	/// <summary>
	/// Tracks error associated with model properties.
	/// </summary>
	public class ErrorContainer
	{
		/// <summary>
		/// Raised when the error for a property changes.
		/// </summary>
		public event EventHandler<ErrorEventArguments> ErrorChanged = delegate {};

		private Dictionary<string, string> m_dicErrors = new Dictionary<string, string>();

		#region Properties

		/// <summary>
		/// Gets the number of errors in the container.
		/// </summary>
		/// <value>The number of errors in the container.</value>
		public Int32 Count
		{
			get
			{
				return m_dicErrors.Count;
			}
		}

		/// <summary>
		/// Gets or sets the error for the specified property.
		/// </summary>
		/// <param name="p_strPropertyName">The name of the property for which to get or set the error.</param>
		/// <returns>The error for the specified property.</returns>
		public string this[string p_strPropertyName]
		{
			get
			{
				if (!m_dicErrors.ContainsKey(p_strPropertyName))
					return null;
				return m_dicErrors[p_strPropertyName];
			}
			set
			{
				if (!m_dicErrors.ContainsKey(p_strPropertyName) || CheckIfChanged(m_dicErrors[p_strPropertyName], value))
				{
					m_dicErrors[p_strPropertyName] = value;
					OnErrorChanged(p_strPropertyName, value);
				}
			}
		}

		#endregion

		/// <summary>
		/// Determins if the given values differ.
		/// </summary>
		/// <typeparam name="T">The type of the values.</typeparam>
		/// <param name="p_tOldValue">The old value.</param>
		/// <param name="p_tNewValue">The new value.</param>
		/// <returns><c>true</c> if the values are different;
		/// <c>false</c> otherwise.</returns>
		protected bool CheckIfChanged<T>(T p_tOldValue, T p_tNewValue)
		{
			if ((p_tOldValue == null) && (p_tNewValue == null))
				return false;
			if ((p_tOldValue == null) || !p_tOldValue.Equals((T)p_tNewValue))
				return true;
			return false;
		}

		/// <summary>
		/// Raises the <see cref="ErrorChanged"/> event.
		/// </summary>
		/// <param name="e">An <see cref="ErrorEventArguments"/> describing the event arguments.</param>
		protected virtual void OnErrorChanged(ErrorEventArguments e)
		{
			ErrorChanged(this, e);
		}

		/// <summary>
		/// Raises the <see cref="ErrorChanged"/> event.
		/// </summary>
		/// <param name="p_strProperty">The property for which the error has changed.</param>
		/// <param name="p_strError">The error.</param>
		protected void OnErrorChanged(string p_strProperty, string p_strError)
		{
			OnErrorChanged(new ErrorEventArguments(p_strProperty, p_strError));
		}

		#region Clear

		/// <summary>
		/// Removes all errors
		/// </summary>
		public void Clear()
		{
			string[] strProperties = new string[m_dicErrors.Keys.Count];
			m_dicErrors.Keys.CopyTo(strProperties, 0);
			m_dicErrors.Clear();
			foreach (string strProperty in strProperties)
				OnErrorChanged(strProperty, null);
		}

		/// <summary>
		/// Removes all errors for the given property.
		/// </summary>
		/// <param name="p_strProperty">The property for which to clear the errors.</param>
		public void Clear(string p_strProperty)
		{
			m_dicErrors.Remove(p_strProperty);
			OnErrorChanged(p_strProperty, null);
		}

		/// <summary>
		/// Removes all errors for the given property.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="p_expProperty">The property for which to clear the errors.</param>
		public void Clear<T>(Expression<Func<T>> p_expProperty)
		{
			Clear(ObjectHelper.GetPropertyName(p_expProperty));
		}

		/// <summary>
		/// Removes all errors for the given property.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="p_expProperty">The property for which to clear the errors.</param>
		public void Clear<T>(Expression<Func<T, object>> p_expProperty)
		{
			Clear(ObjectHelper.GetPropertyName(p_expProperty));
		}

		#endregion

		/// <summary>
		/// Sets the error set for the given property.
		/// </summary>
		/// <param name="p_strProperty">The property for which to set the error.</param>
		/// <param name="p_strValue">The error for the property.</param>
		public void SetError(string p_strProperty, string p_strValue)
		{
			this[p_strProperty] = p_strValue;
		}

		/// <summary>
		/// Gets the error set for the given property.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="p_expProperty">The property for which to retrieve the error.</param>
		/// <returns>The error set for the given property.</returns>
		public string GetError<T>(Expression<Func<T>> p_expProperty)
		{
			return this[ObjectHelper.GetPropertyName(p_expProperty)];
		}

		/// <summary>
		/// Sets the error set for the given property.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="p_expProperty">The property for which to set the error.</param>
		/// <param name="p_strValue">The error for the property.</param>
		public void SetError<T>(Expression<Func<T>> p_expProperty, string p_strValue)
		{
			this[ObjectHelper.GetPropertyName(p_expProperty)] = p_strValue;
		}

		/// <summary>
		/// Gets the error set for the given property.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="p_expProperty">The property for which to retrieve the error.</param>
		/// <returns>The error set for the given property.</returns>
		public string GetError<T>(Expression<Func<T, object>> p_expProperty)
		{
			return this[ObjectHelper.GetPropertyName(p_expProperty)];
		}

		/// <summary>
		/// Sets the error set for the given property.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="p_expProperty">The property for which to set the error.</param>
		/// <param name="p_strValue">The error for the property.</param>
		public void SetError<T>(Expression<Func<T, object>> p_expProperty, string p_strValue)
		{
			this[ObjectHelper.GetPropertyName(p_expProperty)] = p_strValue;
		}
	}
}
