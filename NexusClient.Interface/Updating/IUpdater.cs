using System;
using System.ComponentModel;

namespace Nexus.Client.Updating
{
	/// <summary>
	/// Confirms an action.
	/// </summary>
	/// <param name="p_strMessage">The message describing the action to confirm.</param>
	/// <param name="p_strTitle">The title of the action to confirm.</param>
	/// <returns><c>true</c> if the action has been confirmed;
	/// <c>false</c> otherwise.</returns>
	public delegate bool ConfirmActionMethod(string p_strMessage, string p_strTitle);

	/// <summary>
	/// Describes the methods and properties of an updater.
	/// </summary>
	/// <remarks>
	/// An updater updates a component of the programme.
	/// </remarks>
	public interface IUpdater : INotifyPropertyChanged
	{
		#region Properties

		/// <summary>
		/// Gets the maximum value of the updater's progress.
		/// </summary>
		/// <value>The maximum value of the updater's progress.</value>
		Int32 ProgressMaximum { get; }

		/// <summary>
		/// Gets the updater's progress.
		/// </summary>
		/// <value>The updater's progress.</value>
		Int32 Progress { get; }

		/// <summary>
		/// Gets the updater's message.
		/// </summary>
		/// <remarks>
		/// The updater's message can change throughout the updating process, in order
		/// to reflect the current state.
		/// </remarks>
		/// <value>The updater's message.</value>
		string Message { get; }

		/// <summary>
		/// Gets the updater's name.
		/// </summary>
		/// <value>The updater's name.</value>
		string Name { get; }
		
		/// <summary>
		/// Gets whether the programme needs to be restarted in order for the update to
		/// take effect.
		/// </summary>
		/// <value>Whether the programme needs to be restarted in order for the update to
		/// take effect.</value>
		bool RequiresRestart { get; }

		/// <summary>
		/// Sets the method to use to confirm updater actions.
		/// </summary>
		/// <value>The method to use to confirm updater actions.</value>
		ConfirmActionMethod Confirm { set; }

		#endregion

		/// <summary>
		/// Performs the update.
		/// </summary>
		/// <remarks>
		/// Note that if an update is cancelled, it is consider to have succeeded.
		/// </remarks>
		/// <returns><c>true</c> if the update completed successfully (including being cancelled);
		/// <c>false</c> otherwise.</returns>
		bool Update();

		/// <summary>
		/// Cancels the update.
		/// </summary>
		void Cancel();
	}
}
