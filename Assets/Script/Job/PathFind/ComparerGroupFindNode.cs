using System.Collections.Generic;
using Script.PathFind;

namespace Script.Job.PathFind
{
    public struct ComparerGroupFindNode:IComparer<GroupFindNode>
    {
        public int Compare(GroupFindNode x, GroupFindNode y)
        {
            var xCost = x.GetHeuristicCost();
            var yCost = y.GetHeuristicCost();
            return xCost.CompareTo(yCost);
        }
    }
}