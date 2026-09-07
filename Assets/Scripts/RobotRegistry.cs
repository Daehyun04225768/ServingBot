using System.Collections.Generic;

/// <summary>
/// v1.1.0 — 싱글 윈도우 멀티 에이전트 구조에서 agentId로 RobotController를 조회하기 위한 전역 레지스트리입니다.
/// (기존 v1.0.0은 로봇 1대 = 프로세스 1개였기 때문에 이런 조회가 필요 없었습니다.)
/// </summary>
public static class RobotRegistry
{
    private static readonly Dictionary<int, RobotController> robots = new Dictionary<int, RobotController>();

    public static void Register(int agentId, RobotController controller)
    {
        robots[agentId] = controller;
    }

    public static RobotController Get(int agentId)
    {
        robots.TryGetValue(agentId, out var controller);
        return controller;
    }

    public static IEnumerable<RobotController> All()
    {
        return robots.Values;
    }
}
