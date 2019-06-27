using System.Collections.Generic;
using Harvest.Api;

namespace HarvestToolCore
{
    public class Triple
    {
        public IdNameModel Client { get; set; }
        public IdNameModel Project { get; set; }
        public IdNameModel Task { get; set; }

        public override bool Equals(object value)
        {
            var type = value as Triple;
            return (type != null)
                   && Client.Id == type.Client.Id
                   && Project.Id == type.Project.Id
                   && Task.Id == type.Task.Id; 

        }

        public override int GetHashCode()
        {
            int num = 0x7a2f0b42;
            num = (-1521134295 * num) + ((Client?.Id)??0).GetHashCode();
            num = (-1521134295 * num) + ((Project?.Id) ?? 0).GetHashCode();
            return (-1521134295 * num) + ((Task?.Id) ?? 0).GetHashCode();
        }
    }
}