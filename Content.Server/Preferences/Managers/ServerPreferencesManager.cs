using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._NF.CCVar;
using Content.Shared.CCVar;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Preferences.Managers
{
    /// <summary>
    /// Sends <see cref="MsgPreferencesAndSettings"/> before the client joins the lobby.
    /// Receives <see cref="MsgSelectCharacter"/> and <see cref="MsgUpdateCharacter"/> at any time.
    /// </summary>
    public sealed class ServerPreferencesManager : IServerPreferencesManager, IPostInjectInit
    {
        [Dependency] private readonly IServerNetManager _netManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IServerDbManager _db = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IDependencyCollection _dependencies = default!;
        [Dependency] private readonly ILogManager _log = default!;
        [Dependency] private readonly UserDbDataManager _userDb = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!; // Frontier

        // Cache player prefs on the server so we don't need as much async hell related to them.
        private readonly Dictionary<NetUserId, PlayerPrefData> _cachedPlayerPrefs =
            new();

        private ISawmill _sawmill = default!;

        private int MaxCharacterSlots => _cfg.GetCVar(CCVars.GameMaxCharacterSlots);

        public void Init()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>();
            _netManager.RegisterNetMessage<MsgSelectCharacter>(HandleSelectCharacterMessage);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>(HandleUpdateCharacterMessage);
            _netManager.RegisterNetMessage<MsgDeleteCharacter>(HandleDeleteCharacterMessage);
            _netManager.RegisterNetMessage<MsgUpdateConstructionFavorites>(HandleUpdateConstructionFavoritesMessage);
            _sawmill = _log.GetSawmill("prefs");
        }

        private async void HandleSelectCharacterMessage(MsgSelectCharacter message)
        {
            var index = message.SelectedCharacterIndex;
            var userId = message.MsgChannel.UserId;

            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                _sawmill.Warning($"User {userId} tried to modify preferences before they loaded.");
                return;
            }

            if (index < 0 || index >= MaxCharacterSlots)
            {
                return;
            }

            var curPrefs = prefsData.Prefs!;

            if (!curPrefs.Characters.ContainsKey(index))
            {
                // Non-existent slot.
                return;
            }

            prefsData.Prefs = new PlayerPreferences(curPrefs.Characters, index, curPrefs.AdminOOCColor, curPrefs.ConstructionFavorites);

            if (ShouldStorePrefs(message.MsgChannel.AuthType))
            {
                await _db.SaveSelectedCharacterIndexAsync(message.MsgChannel.UserId, message.SelectedCharacterIndex);
            }
        }

        private async void HandleUpdateCharacterMessage(MsgUpdateCharacter message)
        {
            var userId = message.MsgChannel.UserId;

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (message.Profile == null)
                _sawmill.Error($"User {userId} sent a {nameof(MsgUpdateCharacter)} with a null profile in slot {message.Slot}.");
            else
                await SetProfile(userId, message.Slot, message.Profile);
        }

        public async Task SetProfile(NetUserId userId, int slot, ICharacterProfile profile, bool validateFields = true) // Frontier: add validateFields
        {
            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                _sawmill.Error($"Tried to modify user {userId} preferences before they loaded.");
                return;
            }

            if (slot < 0 || slot >= MaxCharacterSlots)
                return;

            var curPrefs = prefsData.Prefs!;
            var session = _playerManager.GetSessionById(userId);

            profile.EnsureValid(session, _dependencies);

            // Frontier: check for profile modifications (based on Monolith's impl)
            if (validateFields && profile is HumanoidCharacterProfile humanProfile)
            {
                if (curPrefs.Characters.TryGetValue(slot, out var existingProfile) &&
                    existingProfile is HumanoidCharacterProfile humanoidEditingTarget)
                {
                    if (humanProfile.BankBalance != humanoidEditingTarget.BankBalance)
                    {
                        _sawmill.Info($"{session.Name} has tried to modify a character's money (expected: {humanoidEditingTarget.BankBalance} requested: {humanProfile.BankBalance}). They may be using a modified client!");
                        profile = humanProfile.WithBankBalance(humanoidEditingTarget.BankBalance);
                    }
                }
                else
                {
                    if (humanProfile.BankBalance != HumanoidCharacterProfile.DefaultBalance)
                    {
                        _sawmill.Info($"{session.Name} tried to create a character with a non-default balance (expected: {HumanoidCharacterProfile.DefaultBalance} requested: {humanProfile.BankBalance}). They may be using a modified client!");
                        profile = humanProfile.WithBankBalance(HumanoidCharacterProfile.DefaultBalance);
                    }
                }
            }
            // End Frontier: check for profile modifications (based on Monolith's impl)

            var profiles = new Dictionary<int, ICharacterProfile>(curPrefs.Characters)
            {
                [slot] = profile
            };

            prefsData.Prefs = new PlayerPreferences(profiles, slot, curPrefs.AdminOOCColor, curPrefs.ConstructionFavorites);

            if (ShouldStorePrefs(session.Channel.AuthType))
                await _db.SaveCharacterSlotAsync(userId, profile, slot);
        }

        public async Task SetConstructionFavorites(NetUserId userId, List<ProtoId<ConstructionPrototype>> favorites)
        {
            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                _sawmill.Error($"Tried to modify user {userId} preferences before they loaded.");
                return;
            }

            var curPrefs = prefsData.Prefs!;
            prefsData.Prefs = new PlayerPreferences(curPrefs.Characters, curPrefs.SelectedCharacterIndex, curPrefs.AdminOOCColor, favorites);

            var session = _playerManager.GetSessionById(userId);
            if (ShouldStorePrefs(session.Channel.AuthType))
                await _db.SaveConstructionFavoritesAsync(userId, favorites);
        }

        private async void HandleDeleteCharacterMessage(MsgDeleteCharacter message)
        {
            var slot = message.Slot;
            var userId = message.MsgChannel.UserId;

            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                _sawmill.Warning($"User {userId} tried to modify preferences before they loaded.");
                return;
            }

            if (slot < 0 || slot >= MaxCharacterSlots)
            {
                return;
            }

            var curPrefs = prefsData.Prefs!;

            // If they try to delete the slot they have selected then we switch to another one.
            // Of course, that's only if they HAVE another slot.
            int? nextSlot = null;
            if (curPrefs.SelectedCharacterIndex == slot)
            {
                // That ! on the end is because Rider doesn't like .NET 5.
                var (ns, profile) = curPrefs.Characters.FirstOrDefault(p => p.Key != message.Slot)!;
                if (profile == null)
                {
                    // Only slot left, can't delete.
                    return;
                }

                nextSlot = ns;
            }

            var arr = new Dictionary<int, ICharacterProfile>(curPrefs.Characters);
            arr.Remove(slot);

            prefsData.Prefs = new PlayerPreferences(arr, nextSlot ?? curPrefs.SelectedCharacterIndex, curPrefs.AdminOOCColor, curPrefs.ConstructionFavorites);

            if (ShouldStorePrefs(message.MsgChannel.AuthType))
            {
                if (nextSlot != null)
                {
                    await _db.DeleteSlotAndSetSelectedIndex(userId, slot, nextSlot.Value);
                }
                else
                {
                    await _db.SaveCharacterSlotAsync(userId, null, slot);
                }
            }
        }

        private async void HandleUpdateConstructionFavoritesMessage(MsgUpdateConstructionFavorites message)
        {
            var userId = message.MsgChannel.UserId;
            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                _sawmill.Warning($"User {userId} tried to modify preferences before they loaded.");
                return;
            }

            // Validate items in the message so that a modified client cannot freely store a gigabyte of arbitrary data.
            var validatedSet = new HashSet<ProtoId<ConstructionPrototype>>();
            foreach (var favorite in message.Favorites)
            {
                if (_prototypeManager.HasIndex(favorite))
                    validatedSet.Add(favorite);
            }

            var validatedList = message.Favorites;
            if (validatedSet.Count != message.Favorites.Count)
            {
                // A difference in counts indicates that unrecognized or duplicate IDs are present.
                _sawmill.Warning($"User {userId} sent invalid construction favorites.");
                validatedList = validatedSet.ToList();
            }

            var curPrefs = prefsData.Prefs!;
            prefsData.Prefs = new PlayerPreferences(curPrefs.Characters, curPrefs.SelectedCharacterIndex, curPrefs.AdminOOCColor, validatedList);

            if (ShouldStorePrefs(message.MsgChannel.AuthType))
            {
                await _db.SaveConstructionFavoritesAsync(userId, validatedList);
            }
        }

        // Should only be called via UserDbDataManager.
        public async Task LoadData(ICommonSession session, CancellationToken cancel)
        {
            if (!ShouldStorePrefs(session.Channel.AuthType))
            {
                // Don't store data for guests.
                var prefsData = new PlayerPrefData
                {
                    PrefsLoaded = true,
                    Prefs = new PlayerPreferences(
                        new[] { new KeyValuePair<int, ICharacterProfile>(0, HumanoidCharacterProfile.Random()) },
                        0, Color.Transparent, [])
                };

                _cachedPlayerPrefs[session.UserId] = prefsData;
            }
            else
            {
                var prefsData = new PlayerPrefData();
                var loadTask = LoadPrefs();
                _cachedPlayerPrefs[session.UserId] = prefsData;

                await loadTask;

                async Task LoadPrefs()
                {
                    var prefs = await GetOrCreatePreferencesAsync(session.UserId, cancel);
                    prefsData.Prefs = prefs;
                }
            }
        }

        public void FinishLoad(ICommonSession session)
        {
            // This is a separate step from the actual database load.
            // Sanitizing preferences requires play time info due to loadouts.
            // And play time info is loaded concurrently from the DB with preferences.
            var prefsData = _cachedPlayerPrefs[session.UserId];
            DebugTools.Assert(prefsData.Prefs != null);
            prefsData.Prefs = SanitizePreferences(session, prefsData.Prefs, _dependencies);

            prefsData.PrefsLoaded = true;

            var msg = new MsgPreferencesAndSettings();
            msg.Preferences = prefsData.Prefs;
            msg.Settings = new GameSettings
            {
                MaxCharacterSlots = MaxCharacterSlots
            };
            _netManager.ServerSendMessage(msg, session.Channel);

            // Frontier: notify other entities that your player data is loaded.
            if (session.AttachedEntity != null)
                _entityManager.EventBus.RaiseLocalEvent(session.AttachedEntity.Value, new PreferencesLoadedEvent(session, prefsData.Prefs));
        }

        public void OnClientDisconnected(ICommonSession session)
        {
            _cachedPlayerPrefs.Remove(session.UserId);
        }

        public bool HavePreferencesLoaded(ICommonSession session)
        {
            return _cachedPlayerPrefs.ContainsKey(session.UserId);
        }


        /// <summary>
        /// Tries to get the preferences from the cache
        /// </summary>
        /// <param name="userId">User Id to get preferences for</param>
        /// <param name="playerPreferences">The user preferences if true, otherwise null</param>
        /// <returns>If preferences are not null</returns>
        public bool TryGetCachedPreferences(NetUserId userId,
            [NotNullWhen(true)] out PlayerPreferences? playerPreferences)
        {
            if (_cachedPlayerPrefs.TryGetValue(userId, out var prefs))
            {
                playerPreferences = prefs.Prefs;
                return prefs.Prefs != null;
            }

            playerPreferences = null;
            return false;
        }

        /// <summary>
        /// Retrieves preferences for the given username from storage.
        /// </summary>
        public PlayerPreferences GetPreferences(NetUserId userId)
        {
            var prefs = _cachedPlayerPrefs[userId].Prefs;
            if (prefs == null)
            {
                throw new InvalidOperationException("Preferences for this player have not loaded yet.");
            }

            return prefs;
        }

        /// <summary>
        /// Retrieves preferences for the given username from storage or returns null.
        /// </summary>
        public PlayerPreferences? GetPreferencesOrNull(NetUserId? userId)
        {
            if (userId == null)
                return null;

            if (_cachedPlayerPrefs.TryGetValue(userId.Value, out var pref))
                return pref.Prefs;
            return null;
        }

        private async Task<PlayerPreferences> GetOrCreatePreferencesAsync(NetUserId userId, CancellationToken cancel)
        {
            var prefs = await _db.GetPlayerPreferencesAsync(userId, cancel);
            if (prefs is null)
            {
                return await _db.InitPrefsAsync(userId, HumanoidCharacterProfile.Random(), cancel);
            }

            return prefs;
        }

        public async Task RefreshPreferencesAsync(ICommonSession session, CancellationToken cancel)
        {
            if (!_cachedPlayerPrefs.TryGetValue(session.UserId, out var prefsData))
                return;

            var loadTask = LoadPrefs();
            _cachedPlayerPrefs[session.UserId] = prefsData;

            await loadTask;
            return;

            async Task LoadPrefs()
            {
                var prefs = await _db.GetPlayerPreferencesAsync(session.UserId, cancel);

                if (prefs != null)
                {
                    prefsData.Prefs = prefs;
                    prefsData.PrefsLoaded = true;

                    var msg = new MsgPreferencesAndSettings
                    {
                        Preferences = prefs,
                        Settings = new GameSettings
                        {
                            MaxCharacterSlots = MaxCharacterSlots
                        }
                    };

                    _netManager.ServerSendMessage(msg, session.Channel);
                }
            }
        }


        private PlayerPreferences SanitizePreferences(ICommonSession session, PlayerPreferences prefs, IDependencyCollection collection)
        {
            // Clean up preferences in case of changes to the game,
            // such as removed jobs still being selected.

            return new PlayerPreferences(prefs.Characters.Select(p =>
            {
                return new KeyValuePair<int, ICharacterProfile>(p.Key, p.Value.Validated(session, collection));
            }), prefs.SelectedCharacterIndex, prefs.AdminOOCColor, prefs.ConstructionFavorites);
        }

        public IEnumerable<KeyValuePair<NetUserId, ICharacterProfile>> GetSelectedProfilesForPlayers(
            List<NetUserId> usernames)
        {
            return usernames
                .Select(p => (_cachedPlayerPrefs[p].Prefs, p))
                .Where(p => p.Prefs != null)
                .Select(p => new KeyValuePair<NetUserId, ICharacterProfile>(p.p, p.Prefs!.SelectedCharacter));
        }

        internal static bool ShouldStorePrefs(LoginType loginType)
        {
            return loginType.HasStaticUserId();
        }

        private sealed class PlayerPrefData
        {
            public bool PrefsLoaded;
            public PlayerPreferences? Prefs;
        }

        void IPostInjectInit.PostInject()
        {
            _userDb.AddOnLoadPlayer(LoadData);
            _userDb.AddOnFinishLoad(FinishLoad);
            _userDb.AddOnPlayerDisconnect(OnClientDisconnected);
        }
    }

    // Frontier: event for notifying that preferences for a particular player have loaded in.
    public sealed class PreferencesLoadedEvent : EntityEventArgs
    {
        public readonly ICommonSession Session;
        public readonly PlayerPreferences Prefs;

        public PreferencesLoadedEvent(ICommonSession session, PlayerPreferences prefs)
        {
            Session = session;
            Prefs = prefs;
        }
    }
    // End Frontier
}
