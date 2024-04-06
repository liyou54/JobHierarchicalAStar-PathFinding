using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.VisualScripting;

namespace Script.PathFind
{
    public struct UnionFind : IDisposable
    {
        public UnsafeHashMap<GroupId, GroupId> Parent;
        public UnsafeHashMap<GroupId, int> Rank;

        public UnionFind(int size)
        {
            Parent = new UnsafeHashMap<GroupId, GroupId>(size, Allocator.Persistent);
            Rank = new UnsafeHashMap<GroupId, int>(size, Allocator.Persistent);
        }

        public uint Find(GroupId x)
        {
            if (!Parent.ContainsKey(x))
            {
                Parent[x] = x;
                Rank[x] = 0;
                return x;
            }

            uint root = x;
            while (Parent[root] != root)
            {
                root = Parent[root];
            }

            // Path compression
            uint curr = x;
            while (Parent[curr] != root)
            {
                uint next = Parent[curr];
                Parent[curr] = root;
                curr = next;
            }

            return root;
        }

        public UnionFind Copy()
        {
            var unionFind = new UnionFind(Parent.Count);
            foreach (var data in Parent)
            {
                unionFind.Parent[data.Key] = data.Value;
            }
            foreach (var data in Rank)
            {
                unionFind.Rank[data.Key] = data.Value;
            }

            return unionFind;
        }
        
        public void Union(GroupId x, GroupId y)
        {
            GroupId rootX = Find(x);
            GroupId rootY = Find(y);
            if (rootX != rootY)
            {
                Rank.TryAdd(rootX, 0);
                Rank.TryAdd(rootY, 0);

                if (Rank[rootX] < Rank[rootY])
                    Parent[rootX] = rootY;
                else if (Rank[rootX] > Rank[rootY])
                    Parent[rootY] = rootX;
                else
                {
                    Parent[rootY] = rootX;
                    Parent[rootX] = rootX;
                    Rank[rootX]++;
                }
            }
        }


        public UnsafeHashMap<GroupId, UnsafeHashSet<GroupId>> GetChildGraphs()
        {
            var res = new UnsafeHashMap<GroupId, UnsafeHashSet<GroupId>>(Parent.Count, Allocator.TempJob);
            foreach (var data in Parent)
            {
                var parent = Find(data.Key);
                if (!res.TryGetValue(parent, out var set))
                {
                    set = new UnsafeHashSet<GroupId>(Parent.Count, Allocator.TempJob);
                    set.Add(data.Value);
                    res.Add(parent, set);
                }

                set.Add(data.Key);
                res[parent] = set;
            }
            return res;
        }


        public void Dispose()
        {
            Parent.Dispose();
            Rank.Dispose();
        }
    }
}