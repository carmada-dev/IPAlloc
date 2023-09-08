using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IPAlloc
{
    public sealed class IPPool
    {
        private static readonly Regex IPNetworkExpression = new Regex(@"^(?<qualifier>[!]?)(?<ip>(?:\d{1,3}\.){3}\d{1,3})\/(?<mask>[0-9]|[1-2][0-9]|3[0-2])$", RegexOptions.Compiled);

        private static IEnumerable<IPNetwork> ParseNetworks(string environmentIPPools, bool included)
        {
            var ipPools = environmentIPPools
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => IPNetworkExpression.Match(x))
                .Where(m => m.Success);

            foreach (var ipPool in ipPools)
            {
                var ip = IPAddress.Parse(ipPool.Groups["ip"].Value);
                var mask = int.Parse(ipPool.Groups["mask"].Value);
                var qualifier = ipPool.Groups["qualifier"].Value;

                // A qualifier of ! means exclude this pool
                if (qualifier.Equals("!") == !included)
                    yield return IPNetwork.Parse($"{ip}/{mask}");
            }
        }

        public static IPPool Get(string environmentType)
        {
            if (string.IsNullOrEmpty(environmentType))
                throw new ArgumentException($"'{nameof(environmentType)}' cannot be null or empty.", nameof(environmentType));

            return new IPPool(environmentType, Environment.GetEnvironmentVariable($"{nameof(IPPool).ToUpperInvariant()}_{environmentType.Trim().ToUpperInvariant()}") ?? string.Empty);
        }

        private IPPool(string environmentType, string environmentIPPools)
        {
            EnvironmentType = environmentType;
            Included = ParseNetworks(environmentIPPools, true);
            Excluded = ParseNetworks(environmentIPPools, false);
        }

        public string EnvironmentType { get; }

        public IEnumerable<IPNetwork> Included { get; private set; }

        public IEnumerable<IPNetwork> Excluded { get; private set; }

        public override string ToString()
        {
            const string SEPERATOR = ", ";

            var networks = new string[] {
                string.Join(SEPERATOR, Included.Select(x => $"{x}")),
                string.Join(SEPERATOR, Excluded.Select(x => $"!{x}"))
            };

            return $"{EnvironmentType}: {string.Join(SEPERATOR, networks.Where(x => !string.IsNullOrEmpty(x)))}";
        }
    }
}
