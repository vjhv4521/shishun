using System;
using System.Net;
using System.Net.Sockets;

namespace Haven.Networking
{
    public static class LanAddressResolver
    {
        public static string ResolveIPv4()
        {
            try
            {
                IPAddress firstUsable = null;
                var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                foreach (var address in addresses)
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                        continue;
                    var bytes = address.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254)
                        continue;
                    if (IsPrivateIPv4(bytes))
                        return address.ToString();
                    firstUsable ??= address;
                }
                return firstUsable?.ToString() ?? "127.0.0.1";
            }
            catch (Exception)
            {
                return "127.0.0.1";
            }
        }

        public static bool IsPrivateIPv4(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 4)
                return false;
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }
    }
}
