using System;
using Nexus.Client.ModRepositories;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Production Premium-acquisition account provider backed by NMM's existing mod repository session state.
	/// </summary>
	public sealed class ModRepositoryCollectionPremiumAcquisitionAccountProvider : ICollectionPremiumAcquisitionAccountProvider
	{
		private readonly IModRepository _repository;

		/// <summary>
		/// Creates an account provider over the supplied native mod repository.
		/// </summary>
		public ModRepositoryCollectionPremiumAcquisitionAccountProvider(IModRepository repository)
		{
			_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		}

		/// <inheritdoc />
		public CollectionPremiumAcquisitionAccountState Capture()
		{
			RepositoryUserStatus status = _repository.UserStatus;
			bool authenticated = status != null && !_repository.IsOffline;
			return new CollectionPremiumAcquisitionAccountState(
				_repository.GameDomainName,
				authenticated,
				authenticated && status.IsPremium);
		}
	}
}
