namespace Nexus.Client.ModRepositories
{
    using System;

    using Mods;

    public class ModCategory : IModCategory, IEquatable<ModCategory>
	{
		#region Properties

		/// <summary>
		/// Gets or sets the Id of the category.
		/// </summary>
		/// <remarks>The id of the category</remarks>
		public Int32 Id { get; set; }

		/// <summary>
		/// Gets or sets the name of the category.
		/// </summary>
		/// <value>The name of the category.</value>
		public string CategoryName { get; set; }

		/// <summary>
		/// Gets or sets the path to the category.
		/// </summary>
		/// <value>The path to the category.</value>
		public string CategoryPath { get; set; }

		/// <summary>
		/// Gets or sets the number of new mods in the category.
		/// </summary>
		/// <value>The number of new mods in the category.</value>
		public Int32 NewMods { get; set; }

		#endregion

		#region Constructors

		public ModCategory()
		{
			Id = 0;
			CategoryName = "Unassigned";
			CategoryPath = "Unassigned";
			NewMods = 0;
		}

		public ModCategory(Int32 p_intCatId, string p_strCatName, string p_strCatPath)
		{
			Id = p_intCatId;
			CategoryName = p_strCatName;
			CategoryPath = p_strCatPath;
			NewMods = 0;
		}

		public ModCategory(IModCategory p_imcCategory)
		{
			Id = p_imcCategory.Id;
			CategoryName = p_imcCategory.CategoryName;
			CategoryPath = p_imcCategory.CategoryPath;
			NewMods = p_imcCategory.NewMods;
		}

		#endregion

		#region IEquatable

		public override bool Equals(object other)
		{
			return this.Equals(other as ModCategory);
		}

		public bool Equals(ModCategory other)
		{
			return (other != null &&
					other.Id == this.Id &&
					other.CategoryName == this.CategoryName);
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}
		#endregion
	}
}
