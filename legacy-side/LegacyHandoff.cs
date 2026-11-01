// DRAFT for Visual Studio 2008 (C# 2.0 syntax only; written without a compiler, please build and test).
// Add this file to DDReviewAppCode\App_Code. It verifies the signed one-time ticket minted by the new site
// (format and test vectors: tests/DDReview.Tests/Legacy/LegacyTicketTests.cs in the new repo).
using System;
using System.Collections;
using System.Configuration;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Configuration;

namespace DDReview {
	public class LegacyHandoff {
		private const int MinSecretLength = 32;
		private const int ClockSkewSeconds = 5;
		private static readonly Hashtable UsedNonces = new Hashtable();   // nonce -> expiry (unix seconds); a ticket works once
		private static readonly object NonceLock = new object();

		// Both settings live in this site's own web.config (never in source control):
		//   <add key="HandoffSecret" value="(same 32+ character secret as the new site's LegacyProxy:HandoffSecret)"/>
		public static string Secret {
			get { return WebConfigurationManager.AppSettings["HandoffSecret"]; }
			}
		public static bool Enabled {
			get { return Secret != null && Secret.Length >= MinSecretLength; }
			}

		// True when the ticket is genuine, unexpired and unused. Gives back the web user it was minted for.
		public static bool TryVerify(string secret, string ticket, DateTime utcNow, out int webUserId) {
			webUserId = 0;
			if (secret == null || secret.Length < MinSecretLength || ticket == null || ticket.Length == 0 || ticket.Length > 200)
				return false;
			string[] parts = ticket.Split('.');
			if (parts.Length != 5 || parts[0] != "v1")
				return false;
			if (!IsLowerHex(parts[3], 32) || !IsLowerHex(parts[4], 64))
				return false;
			int uid;
			long expiry;
			if (!TryParseDigits(parts[1], out uid) || uid <= 0 || !TryParseLong(parts[2], out expiry))
				return false;
			string body = parts[0] + "." + parts[1] + "." + parts[2] + "." + parts[3];
			if (!ConstantTimeEquals(Sign(secret, body), parts[4]))
				return false;
			long now = ToUnixSeconds(utcNow);
			if (now > expiry + ClockSkewSeconds)
				return false;
			lock (NonceLock) {
				ArrayList expired = new ArrayList();
				foreach (DictionaryEntry de in UsedNonces)
					if ((long)de.Value + ClockSkewSeconds < now)
						expired.Add(de.Key);
				foreach (object k in expired)
					UsedNonces.Remove(k);
				if (UsedNonces.ContainsKey(parts[3]))
					return false;
				UsedNonces[parts[3]] = expiry;
				}
			webUserId = uid;
			return true;
			}

		// Looks up the user name and password the existing login expects, for a person the new site has already signed in.
		// (The central master still holds them; see docs/LEGACY-OLDSIDE.md for what happens when that changes.)
		public static bool TryGetCredentials(int webUserId, out string userName, out string password) {
			userName = null;
			password = null;
			SqlConnection cn = new SqlConnection(WebConfigurationManager.ConnectionStrings["DSN"].ToString());
			try {
				cn.Open();
				SqlCommand cmd = new SqlCommand("select UserName, [Password] from dbo.tblWebUserSecurity where WebUserID = @id", cn);
				cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
				SqlDataReader dr = cmd.ExecuteReader();
				if (dr.Read() && !dr.IsDBNull(0) && !dr.IsDBNull(1)) {
					userName = dr.GetString(0);
					password = dr.GetString(1);
					}
				dr.Close();
				}
			finally {
				cn.Close();
				}
			return (userName != null);
			}

		// ReturnUrl must stay inside this application ("/classic/..."): it is where Forms authentication sends the person after login.
		public static bool IsLocalReturnUrl(string returnUrl) {
			if (returnUrl == null || returnUrl.Length == 0 || returnUrl.Length > 300)
				return false;
			string root = HttpRuntime.AppDomainAppVirtualPath;      // "/classic"
			if (!root.EndsWith("/"))
				root += "/";
			if (!returnUrl.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				return false;
			return (returnUrl.IndexOf("//") < 0 && returnUrl.IndexOf('\\') < 0 && returnUrl.IndexOf("..") < 0);
			}

		private static string Sign(string secret, string body) {
			HMACSHA256 h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
			byte[] mac = h.ComputeHash(Encoding.UTF8.GetBytes(body));
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < mac.Length; i++)
				sb.Append(mac[i].ToString("x2"));
			return sb.ToString();
			}
		private static bool ConstantTimeEquals(string a, string b) {
			if (a.Length != b.Length)
				return false;
			int diff = 0;
			for (int i = 0; i < a.Length; i++)
				diff |= (a[i] ^ b[i]);
			return (diff == 0);
			}
		private static bool IsLowerHex(string s, int length) {
			if (s.Length != length)
				return false;
			for (int i = 0; i < s.Length; i++) {
				char c = s[i];
				if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
					return false;
				}
			return true;
			}
		private static bool TryParseDigits(string s, out int value) {
			value = 0;
			if (s.Length == 0 || s.Length > 9)
				return false;
			for (int i = 0; i < s.Length; i++)
				if (s[i] < '0' || s[i] > '9')
					return false;
			value = int.Parse(s);
			return true;
			}
		private static bool TryParseLong(string s, out long value) {
			value = 0;
			if (s.Length == 0 || s.Length > 12)
				return false;
			for (int i = 0; i < s.Length; i++)
				if (s[i] < '0' || s[i] > '9')
					return false;
			value = long.Parse(s);
			return true;
			}
		private static long ToUnixSeconds(DateTime utc) {
			return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
			}
		}
	}
