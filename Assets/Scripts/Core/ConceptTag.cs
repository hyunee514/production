namespace AutoSurvival.Core
{
    /// <summary>자동화설비기능사 출제 개념 태그. 퍼즐은 반드시 1개 이상의 태그를 가진다.</summary>
    public enum ConceptTag
    {
        PneumaticValve,     // 공압 방향제어밸브 (3/2, 5/2, 5/3)
        PneumaticCylinder,  // 단동/복동 실린더
        HydraulicPressure,  // 유압 (파스칼 원리, 릴리프 밸브)
        PipeFitting,        // 배관 이음 (엘보, 티, 유니온)
        LadderSelfHold,     // PLC 자기유지 회로
        LadderInterlock,    // 인터록 (상호 배타)
        LadderTimer,        // 타이머 (TON)
        Sensor,             // 센서 (근접, 광전)
        Safety,             // 안전수칙
        PneumaticSupply     // 공압 공급장치 (압축기, 서비스 유닛 FRL)
    }
}
