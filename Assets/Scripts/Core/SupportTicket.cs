using System;
using System.Text;

namespace Game.Core
{
    /// <summary>
    /// The mail a player sends when something has gone wrong, assembled before their mail app opens.
    ///
    /// A support mail with no diagnostics in it costs two round trips before the desk knows which build
    /// on which handset is being described — and the player who sent it has usually stopped reading by
    /// then. So the body arrives filled in: the id, the build, the device and the language, above a
    /// blank line for the player's own words.
    ///
    /// The block is written in English regardless of the game's language, because it is read by whoever
    /// answers the mail, not by the sender. Only the one line inviting them to type is localised, and it
    /// is passed in.
    ///
    /// Pure: it takes strings and returns strings. Everything it describes — version, device, platform —
    /// is read from <c>UnityEngine.Application</c> and <c>SystemInfo</c> by
    /// <c>Game.Systems.PlayerIdentity</c>, which is also the only place a URL is ever opened.
    /// </summary>
    public static class SupportTicket
    {
        /// <summary>The line the desk greps for; also what keeps a forwarded mail identifiable.</summary>
        private const string Fence = "-----";

        /// <summary>
        /// Everything the desk needs, then a blank line, then the invitation to write.
        ///
        /// The prompt goes at the BOTTOM and the diagnostics at the top, which is the opposite of what
        /// looks tidy. Mail apps open with the cursor at the end of the body: putting the invitation last
        /// means the player is already typing where they were asked to.
        /// </summary>
        public static string Body(string playerId, string version, string build, string platform,
                                  string device, string language, int saveVersion, string prompt)
        {
            var sb = new StringBuilder(256);
            sb.Append(Fence).Append('\n');
            Line(sb, "id", playerId);
            Line(sb, "app", Join(version, build));
            Line(sb, "platform", platform);
            Line(sb, "device", device);
            Line(sb, "lang", language);
            Line(sb, "save", "v" + saveVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(Fence).Append('\n').Append('\n');
            if (!string.IsNullOrEmpty(prompt)) sb.Append(prompt).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// The subject, carrying the id as well so a mail whose body was retyped or stripped by a client
        /// still names the save it came from.
        /// </summary>
        public static string Subject(string caption, string playerId)
        {
            if (string.IsNullOrEmpty(caption)) caption = "Support";
            return string.IsNullOrEmpty(playerId) ? caption : caption + " [" + playerId + "]";
        }

        /// <summary>
        /// A <c>mailto:</c> URL, or the empty string when there is no address worth opening one for.
        ///
        /// SUBJECT AND BODY ARE ESCAPED, THE ADDRESS IS CHECKED. Escaping the address would break the
        /// <c>@</c>, and unescaping it back again would put us in the business of deciding which
        /// characters are safe — so an address that is not a plain one is refused outright instead. That
        /// matters because a newline inside an address is how a mailto is made to carry headers nobody
        /// typed: the field is authored in the Inspector today, but a refusal costs nothing and the field
        /// will outlive the reason it was safe.
        /// </summary>
        public static string Mailto(string address, string subject, string body)
        {
            if (!IsPlainAddress(address)) return string.Empty;

            var sb = new StringBuilder(256);
            sb.Append("mailto:").Append(address);
            char join = '?';
            if (!string.IsNullOrEmpty(subject))
            {
                sb.Append(join).Append("subject=").Append(Uri.EscapeDataString(subject));
                join = '&';
            }
            if (!string.IsNullOrEmpty(body))
                sb.Append(join).Append("body=").Append(Uri.EscapeDataString(body));
            return sb.ToString();
        }

        /// <summary>
        /// One address, no display name, no comma-separated second recipient, nothing that could be read
        /// as a header. Deliberately narrower than RFC 5321 allows: every address this ever sees is one
        /// somebody typed into the Inspector.
        /// </summary>
        public static bool IsPlainAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;

            int at = -1;
            for (int i = 0; i < address.Length; i++)
            {
                char c = address[i];
                if (c == '@')
                {
                    if (at >= 0) return false;   // two of them is not one address
                    at = i;
                    continue;
                }
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '.' || c == '_' || c == '-' || c == '+';
                if (!ok) return false;
            }

            // Something before the @, and a dotted something after it.
            if (at <= 0 || at >= address.Length - 1) return false;
            int dot = address.IndexOf('.', at);
            return dot > at + 1 && dot < address.Length - 1;
        }

        private static void Line(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append(": ").Append(string.IsNullOrEmpty(value) ? "?" : value).Append('\n');
        }

        private static string Join(string version, string build)
        {
            if (string.IsNullOrEmpty(version)) version = "?";
            return string.IsNullOrEmpty(build) ? version : version + " (" + build + ")";
        }
    }
}
