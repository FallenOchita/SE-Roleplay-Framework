using System;
using System.Linq;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core.Abstractions;
using SE.Roleplay.Framework.Core.Identity;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace SE.Roleplay.Framework.Torch.Commands
{
    /// <summary>
    /// Player-facing character commands. All data is read from the active Torch instance's
    /// framework context, so character ownership and persistence remain server-scoped.
    /// </summary>
    [Category("rp character")]
    public sealed class CharacterCommandModule : CommandModule
    {
        internal static IFrameworkContext? Framework { get; set; }

        [Permission(MyPromoteLevel.None)]
        [Command("list", "Lists your characters on this server.")]
        public async void List()
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;

            try
            {
                var characters = await RequireFramework().Profiles.GetCharactersAsync(steamId).ConfigureAwait(false);
                if (characters.Count == 0)
                {
                    Respond("You do not have any characters on this server. Use !rp character create \"Name\" to make one.");
                    return;
                }

                var selectedId = default(Guid?);
                if (RequireFramework().Players.TryGet(steamId, out PlayerIdentity identity))
                    selectedId = identity.CharacterId;

                var lines = characters.Select((character, index) =>
                    (selectedId == character.CharacterId ? "* " : "") +
                    (index + 1) + ". " + character.Name +
                    (string.IsNullOrWhiteSpace(character.Description) ? "" : " — " + character.Description));

                Respond("Your server characters:\n" + string.Join("\n", lines));
            }
            catch (Exception exception)
            {
                Respond("Could not list characters: " + exception.Message);
            }
        }

        [Permission(MyPromoteLevel.None)]
        [Command("create", "Creates a character on this server.")]
        public async void Create(string name, string description = "")
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;

            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 32)
            {
                Respond("Character names must be between 1 and 32 characters.");
                return;
            }

            if (description != null && description.Length > 160)
            {
                Respond("Character descriptions must be 160 characters or fewer.");
                return;
            }

            try
            {
                var character = await RequireFramework().Profiles
                    .CreateCharacterAsync(steamId, name, description ?? string.Empty)
                    .ConfigureAwait(false);
                Respond("Created character " + character.Name + " on this server.");
            }
            catch (Exception exception)
            {
                Respond("Could not create character: " + exception.Message);
            }
        }

        [Permission(MyPromoteLevel.None)]
        [Command("select", "Selects one of your characters by its number in the list.")]
        public async void Select(int number)
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;

            try
            {
                var profiles = RequireFramework().Profiles;
                var characters = (await profiles.GetCharactersAsync(steamId).ConfigureAwait(false)).ToArray();
                if (number < 1 || number > characters.Length)
                {
                    Respond("Choose a character number from 1 to " + characters.Length + ".");
                    return;
                }

                var character = characters[number - 1];
                await profiles.SelectCharacterAsync(steamId, character.CharacterId).ConfigureAwait(false);
                Respond("Selected " + character.Name + " for this server.");
            }
            catch (Exception exception)
            {
                Respond("Could not select character: " + exception.Message);
            }
        }

        [Permission(MyPromoteLevel.None)]
        [Command("delete", "Deletes one of your characters by its number in the list.")]
        public async void Delete(int number)
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;

            try
            {
                var profiles = RequireFramework().Profiles;
                var characters = (await profiles.GetCharactersAsync(steamId).ConfigureAwait(false)).ToArray();
                if (number < 1 || number > characters.Length)
                {
                    Respond("Choose a character number from 1 to " + characters.Length + ".");
                    return;
                }

                var character = characters[number - 1];
                await profiles.DeleteCharacterAsync(steamId, character.CharacterId).ConfigureAwait(false);
                Respond("Deleted " + character.Name + " from this server.");
            }
            catch (Exception exception)
            {
                Respond("Could not delete character: " + exception.Message);
            }
        }

        [Permission(MyPromoteLevel.None)]
        [Command("rename", "Renames one of your characters by its number in the list.")]
        public async void Rename(int number, string newName)
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;
            if (string.IsNullOrWhiteSpace(newName) || newName.Trim().Length > 32)
            {
                Respond("Character names must be between 1 and 32 characters.");
                return;
            }

            try
            {
                var profiles = RequireFramework().Profiles;
                var character = (await profiles.GetCharactersAsync(steamId).ConfigureAwait(false)).ElementAtOrDefault(number - 1);
                if (number < 1 || character == null)
                {
                    Respond("Choose a character number from the current list.");
                    return;
                }
                await profiles.UpdateCharacterAsync(steamId, character.CharacterId, newName, character.Description).ConfigureAwait(false);
                Respond("Renamed character to " + newName.Trim() + ".");
            }
            catch (Exception exception) { Respond("Could not rename character: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.None)]
        [Command("describe", "Changes the description of one of your characters by its number in the list.")]
        public async void Describe(int number, string description)
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;
            if (description != null && description.Length > 160)
            {
                Respond("Character descriptions must be 160 characters or fewer.");
                return;
            }

            try
            {
                var profiles = RequireFramework().Profiles;
                var character = (await profiles.GetCharactersAsync(steamId).ConfigureAwait(false)).ElementAtOrDefault(number - 1);
                if (number < 1 || character == null)
                {
                    Respond("Choose a character number from the current list.");
                    return;
                }
                await profiles.UpdateCharacterAsync(steamId, character.CharacterId, character.Name, description ?? string.Empty).ConfigureAwait(false);
                Respond("Updated the description for " + character.Name + ".");
            }
            catch (Exception exception) { Respond("Could not update character description: " + exception.Message); }
        }


        [Permission(MyPromoteLevel.None)]
        [Command("meta", "Sets a metadata field on one of your characters.")]
        public async void SetMetadata(int number, string key, string value)
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;
            try
            {
                var profiles = RequireFramework().Profiles;
                var character = (await profiles.GetCharactersAsync(steamId).ConfigureAwait(false)).ElementAtOrDefault(number - 1);
                if (number < 1 || character == null) { Respond("Choose a character number from the current list."); return; }
                await profiles.SetCharacterMetadataAsync(steamId, character.CharacterId, key, value).ConfigureAwait(false);
                Respond("Saved metadata '" + key + "' for " + character.Name + ".");
            }
            catch (Exception exception) { Respond("Could not save character metadata: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.None)]
        [Command("metadel", "Removes a metadata field from one of your characters.")]
        public async void RemoveMetadata(int number, string key)
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;
            try
            {
                var profiles = RequireFramework().Profiles;
                var character = (await profiles.GetCharactersAsync(steamId).ConfigureAwait(false)).ElementAtOrDefault(number - 1);
                if (number < 1 || character == null) { Respond("Choose a character number from the current list."); return; }
                await profiles.SetCharacterMetadataAsync(steamId, character.CharacterId, key, null).ConfigureAwait(false);
                Respond("Removed metadata '" + key + "' from " + character.Name + ".");
            }
            catch (Exception exception) { Respond("Could not remove character metadata: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.None)]
        [Command("metalist", "Lists metadata for one of your characters.")]
        public async void ListMetadata(int number)
        {
            var steamId = GetSteamId();
            if (steamId == 0) return;
            try
            {
                var character = (await RequireFramework().Profiles.GetCharactersAsync(steamId).ConfigureAwait(false)).ElementAtOrDefault(number - 1);
                if (number < 1 || character == null) { Respond("Choose a character number from the current list."); return; }
                var entries = character.Metadata.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => entry.Key + " = " + entry.Value).ToArray();
                Respond(entries.Length == 0 ? character.Name + " has no metadata fields." :
                    "Metadata for " + character.Name + ":\n" + string.Join("\n", entries));
            }
            catch (Exception exception) { Respond("Could not list character metadata: " + exception.Message); }
        }

        private ulong GetSteamId()
        {
            var steamId = Context.Player?.SteamUserId ?? 0;
            if (steamId == 0)
                Respond("These commands must be used by a player in-game.");
            return steamId;
        }

        private static IFrameworkContext RequireFramework()
        {
            return Framework ?? throw new InvalidOperationException("The roleplay framework is not initialized.");
        }

        private void Respond(string message)
        {
            // Torch chat responses are dispatched through the host's game-thread invoker.
            Context.Torch.Invoke(() => Context.Respond(message));
        }
    }
}
