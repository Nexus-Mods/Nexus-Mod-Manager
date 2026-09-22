using System;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Composes canonical target resolution and one C6.1 native-state capture into the production additive resolved-plan builder.
	/// </summary>
	/// <remarks>
	/// This service performs read-only target/native-state observation plus Collections metadata journaling only. It does not acquire
	/// member archives, translate Collection recipes, create native installer tasks or mutate the game/native ownership stores.
	/// </remarks>
	public sealed class CollectionAdditivePlanPreparationService
	{
		private readonly CollectionTargetIdentityResolver _targetIdentityResolver;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionResolvedPlanBuilder _resolvedPlanBuilder;

		/// <summary>
		/// Creates the C6.15.4 preparation bridge over the existing C4 target resolver, C6.1 state reader and resolved-plan builder.
		/// </summary>
		public CollectionAdditivePlanPreparationService(CollectionTargetIdentityResolver targetIdentityResolver,
			CollectionNativeStateReader nativeStateReader, CollectionResolvedPlanBuilder resolvedPlanBuilder)
		{
			_targetIdentityResolver = targetIdentityResolver ?? throw new ArgumentNullException(nameof(targetIdentityResolver));
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_resolvedPlanBuilder = resolvedPlanBuilder ?? throw new ArgumentNullException(nameof(resolvedPlanBuilder));
		}

		/// <summary>
		/// Resolves the canonical target/storage authority, captures exactly one C6.1 state index and builds a new additive plan.
		/// </summary>
		public CollectionAdditivePlanBuildResult Prepare(CollectionEffectiveSelection effectiveSelection, GameStoragePathSet targetPaths)
		{
			if (effectiveSelection == null)
				throw new ArgumentNullException(nameof(effectiveSelection));
			if (targetPaths == null)
				throw new ArgumentNullException(nameof(targetPaths));

			CollectionTargetAuthority authority = _targetIdentityResolver.Resolve(targetPaths);
			CollectionNativeStateIndex nativeState = _nativeStateReader.Capture(authority.Target);
			return _resolvedPlanBuilder.Build(effectiveSelection, authority.Target, nativeState);
		}
	}
}
