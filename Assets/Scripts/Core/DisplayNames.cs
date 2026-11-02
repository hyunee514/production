using AutoSurvival.Machines;

namespace AutoSurvival.Core
{
    /// <summary>화면·대사에 쓰는 한글 이름 (UnityEngine 의존 없음).</summary>
    public static class DisplayNames
    {
        public static string Concept(ConceptTag tag) => tag switch
        {
            ConceptTag.PneumaticValve => "공압 밸브",
            ConceptTag.PneumaticCylinder => "공압 실린더",
            ConceptTag.HydraulicPressure => "유압(파스칼의 원리)",
            ConceptTag.PipeFitting => "배관 이음",
            ConceptTag.LadderSelfHold => "래더 자기유지",
            ConceptTag.LadderInterlock => "래더 인터록",
            ConceptTag.LadderTimer => "래더 타이머",
            ConceptTag.Sensor => "센서",
            ConceptTag.Safety => "안전",
            ConceptTag.PneumaticSupply => "공압 공급(서비스 유닛)",
            _ => tag.ToString()
        };

        public static string Machine(MachineKind kind) => kind switch
        {
            MachineKind.WaterPurifier => "정수 플랜트",
            MachineKind.Smelter => "제련기",
            MachineKind.PowerGenerator => "발전기",
            MachineKind.AutoGarden => "자동 재배기",
            MachineKind.CraneLift => "크레인",
            _ => kind.ToString()
        };
    }
}
