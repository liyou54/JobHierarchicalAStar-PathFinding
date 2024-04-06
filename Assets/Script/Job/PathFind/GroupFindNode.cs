using Script.PathFind;

namespace Script.Job.PathFind
{
    public struct GroupFindNode
    {
        public GroupInfo GroupInfo;
        // 上一步启发式代价
        public float HeuristicCostFromLastStep;
        // 上一步真实代价
        public float ActualCostFromLastStep;
        // 还剩下的启发式代价
        public float RemainingHeuristicCost;
        // 上一步为止所使用的代价
        public float ActualCostUpToLastStep;
        
        public float GetHeuristicCost()
        {
            return HeuristicCostFromLastStep + RemainingHeuristicCost + ActualCostUpToLastStep;
        }
        
        public float GetActualCost()
        {
            return ActualCostFromLastStep + ActualCostUpToLastStep;
        }
        
    }
}