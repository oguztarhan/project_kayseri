using System;

namespace Game.Core
{
    /// <summary>
    /// The number a player reads out to support, and nothing else.
    ///
    /// It is NOT an account, a login or a cloud key — the game has no server to check one against. It
    /// is a label minted on the device so that a mail from a stranger can be matched to the save that
    /// produced it. That is the whole job, and it is why the id is deliberately short: twelve symbols
    /// a person can copy, paste, and if it comes to it read down a phone line.
    ///
    /// THE ALPHABET HAS NO LOOK-ALIKES. 0/O and 1/I/L are the pairs support desks lose time to, so the
    /// alphabet is the thirty symbols left when they are taken out. Twelve of those carry close to
    /// sixty bits, which is more than a game needs and still fits in three groups of four.
    ///
    /// Pure by design: no clock, no PlayerPrefs, no <c>UnityEngine</c>. The entropy arrives as a
    /// <see cref="Guid"/> argument, so a test can mint a known id and the mint itself has nothing to
    /// stub. Storage and the decision of when to mint live in <c>Game.Systems.PlayerIdentity</c>.
    /// </summary>
    public static class PlayerId
    {
        /// <summary>Crockford's set minus the vowels' worst offenders: no 0, 1, I, L, O, U.</summary>
        private const string Symbols = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

        private const int Groups = 3;
        private const int GroupLength = 4;
        private const char Separator = '-';

        /// <summary>Symbols in an id, ignoring the separators.</summary>
        public const int Length = Groups * GroupLength;

        /// <summary>Characters in a formatted id, separators included: <c>ABCD-EFGH-JKMN</c>.</summary>
        public const int FormattedLength = Length + Groups - 1;

        /// <summary>
        /// A formatted id derived from <paramref name="guid"/>.
        ///
        /// The guid's sixteen bytes are folded in half with XOR rather than truncated. Truncating would
        /// have kept whichever half the platform happens to fill with a timestamp or a MAC address in a
        /// version-1 guid, and two devices minting in the same second would then share a prefix. Folding
        /// spreads whatever entropy there is across every symbol drawn.
        /// </summary>
        public static string From(Guid guid)
        {
            byte[] bytes = guid.ToByteArray();

            ulong folded = 0UL;
            for (int i = 0; i < 8; i++)
                folded = (folded << 8) | (byte)(bytes[i] ^ bytes[i + 8]);

            var buffer = new char[FormattedLength];
            int at = 0;
            for (int i = 0; i < Length; i++)
            {
                if (i > 0 && i % GroupLength == 0) buffer[at++] = Separator;
                buffer[at++] = Symbols[(int)(folded % (ulong)Symbols.Length)];
                folded /= (ulong)Symbols.Length;
            }
            return new string(buffer);
        }

        /// <summary>
        /// True when <paramref name="id"/> is one this build minted and can show as-is.
        ///
        /// Used to decide whether a save already carries an id, so it is deliberately strict about the
        /// shape as well as the symbols: an id from a save written by hand, truncated by a bad merge or
        /// left over from a format we no longer use is not repaired, it is replaced. A player who has
        /// never quoted their id anywhere loses nothing by being given a new one; a player who has
        /// quoted it was shown a valid one, and a valid one still passes here.
        /// </summary>
        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != FormattedLength) return false;

            for (int i = 0, group = 0; i < id.Length; i++)
            {
                if (group == GroupLength)
                {
                    if (id[i] != Separator) return false;
                    group = 0;
                    continue;
                }
                if (Symbols.IndexOf(id[i]) < 0) return false;
                group++;
            }
            return true;
        }
    }
}
