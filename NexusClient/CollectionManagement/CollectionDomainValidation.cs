using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Validation shared by collection domain metadata models.
	/// </summary>
	internal static class CollectionDomainValidation
	{
		public static string RequireDisplayValue(string value, string parameterName)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("A display value is required.", parameterName);
			if (!StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("Display values must not contain leading or trailing whitespace.", parameterName);

			return value;
		}

		public static string OptionalDisplayValue(string value, string parameterName)
		{
			if (value == null)
				return null;

			return RequireDisplayValue(value, parameterName);
		}
	}
}
