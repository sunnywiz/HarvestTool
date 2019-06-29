using System;
using System.Collections.Generic;
using System.Linq;

namespace HarvestToolCore
{
    public class Griddle<T1, T2, TV> 
    {
        private SortedSet<T2> _k2;
        private Dictionary<T1,Dictionary<T2,TV>> _values; 

        public Griddle()
        {
            _values = new Dictionary<T1, Dictionary<T2, TV>>();
            _k2 = new SortedSet<T2>();
        }

        public void Set(T1 k1, T2 k2, TV val)
        {
            if (!_k2.Contains(k2)) _k2.Add(k2);
            if (!_values.ContainsKey(k1)) _values[k1] = new Dictionary<T2, TV>();
            var x1 = _values[k1];
            x1[k2] = val; 
        }

        public TV Get(T1 k1, T2 k2)
        {
            if (!_values.TryGetValue(k1, out var x1))
            {
                return default(TV);
            }

            if (!x1.TryGetValue(k2, out var x2))
            {
                return default(TV);
            }

            return x2;
        }

        public void Update(T1 k1, T2 k2, Func<TV,TV> update)
        {
            var v1 = Get(k1, k2);
            v1 = update(v1);
            Set(k1, k2, v1); 
        }

        public List<T1> Keys1 => _values.Keys.ToList();
        public List<T2> Keys2 => _k2.ToList(); 
    }
}