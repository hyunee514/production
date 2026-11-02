using System.Collections.Generic;
using AutoSurvival.Core;
using AutoSurvival.Puzzles;

namespace AutoSurvival.Data
{
    /// <summary>
    /// 개발 초기 시드 데이터. 문제는 자동화설비기능사 출제 범위(공압·유압·배관·PLC·센서·안전)에 맞춰
    /// 새로 쓴 자체 문제(Original)이며 실제 기출 원문이 아니다. 실제 기출은 CSV 임포터로 PastExam으로 추가한다.
    /// </summary>
    public static class DefaultScenarioData
    {
        private static ExamQuestion Q(string id, ConceptTag concept, int difficulty, string question,
                                      string[] choices, int answerIndex, string explanation, string fieldNote) =>
            new ExamQuestion
            {
                id = id, origin = QuestionOrigin.Original, examLabel = "", concept = concept,
                difficulty = difficulty, question = question, choices = choices, answerIndex = answerIndex,
                explanation = explanation, fieldNote = fieldNote
            };

        public static List<ExamQuestion> Questions() => new()
        {
            // ── 공압 ──
            Q("q.pneu.cyl.single", ConceptTag.PneumaticCylinder, 1,
              "스프링의 힘으로 피스톤 로드를 복귀시키는 공압 실린더는?",
              new[] { "단동 실린더", "복동 실린더", "탠덤 실린더", "양로드 실린더" }, 0,
              "단동 실린더는 한쪽에만 압축공기를 공급하고 복귀는 내장 스프링이 담당한다.",
              "방금 피스톤이 공기를 빼자 스르륵 돌아왔지? 스프링으로 돌아오는 구조가 단동 실린더야."),
            Q("q.pneu.valve.52", ConceptTag.PneumaticValve, 1,
              "방향제어밸브 '5/2 way 밸브' 표기에서 5와 2가 뜻하는 것은?",
              new[] { "포트(접속구) 수 5, 제어 위치 수 2", "제어 위치 수 5, 포트 수 2", "관 지름 5mm, 2mm", "사용 압력 5bar, 2bar" }, 0,
              "방향제어밸브는 '포트 수 / 위치 수' 순서로 표기한다. 5/2는 5포트 2위치 밸브다.",
              "밸브 패널에 구멍이 다섯 개 있었지? 그게 5포트, 레버 위치가 두 개라서 5/2야."),
            Q("q.pneu.frl", ConceptTag.PneumaticSupply, 2,
              "공기압 조정 유닛(서비스 유닛)을 구성하는 기기가 아닌 것은?",
              new[] { "공기 필터", "압력 조절기(레귤레이터)", "루브리케이터", "소음기(머플러)" }, 3,
              "서비스 유닛은 필터, 레귤레이터, 루브리케이터로 구성된다. 소음기는 배기구에 다는 별도 기기다.",
              "압축기 바로 뒤에 달린 세 개짜리 덩어리 기억나? 필터·레귤레이터·루브리케이터야."),

            Q("q.pneu.cyl.double", ConceptTag.PneumaticCylinder, 2,
              "전진과 후진 양쪽 행정 모두 압축공기의 힘으로 움직이는 실린더는?",
              new[] { "복동 실린더", "단동 실린더", "스프링 복귀 실린더", "다이어프램 실린더" }, 0,
              "복동 실린더는 피스톤 양쪽 챔버에 번갈아 공기를 넣어 전진·후진 모두 공기압으로 구동한다.",
              "방금 실린더는 스프링 없이 양쪽을 번갈아 밀었어. 그게 복동 실린더야."),
            Q("q.pneu.muffler", ConceptTag.PneumaticValve, 2,
              "방향제어밸브의 배기구에 소음기(머플러)를 설치하는 주된 목적은?",
              new[] { "배기 소음을 줄이고 이물질 유입을 막는다", "공급 압력을 높인다", "실린더 속도를 두 배로 한다", "공기를 건조시킨다" }, 0,
              "소음기는 배기 소음을 줄이고 먼지 유입을 막는다. 막히면 배기 저항이 커져 실린더 동작이 느려지거나 멈춘다.",
              "소음기가 막히니까 실린더가 끝까지 못 갔지? 갇힌 공기가 버티기 때문이야."),
            Q("q.pneu.bistable", ConceptTag.PneumaticValve, 3,
              "양측 전자(또는 양측 파일럿) 5/2 밸브의 특징으로 옳은 것은?",
              new[] { "신호를 한 번 주면 반대 신호가 올 때까지 위치를 유지한다", "신호를 끊으면 즉시 스프링으로 복귀한다", "항상 중립 위치에서 모든 포트가 닫힌다", "공급 압력이 없어야만 전환된다" }, 0,
              "양측 제어 밸브는 메모리 기능이 있어 마지막에 준 신호의 위치를 유지한다. 펄스 신호로도 구동할 수 있다.",
              "14번 신호를 한 번만 줬는데도 도어가 열린 채 있었지? 밸브가 위치를 기억한 거야."),
            Q("q.pneu.pressure", ConceptTag.PneumaticCylinder, 2,
              "공압 실린더의 이론 출력(F)을 구하는 식은? (P: 압력, A: 피스톤 단면적)",
              new[] { "F = P × A", "F = P ÷ A", "F = A ÷ P", "F = P + A" }, 0,
              "힘은 압력과 면적의 곱이다. 로드쪽은 로드 단면적만큼 면적이 줄어 후진 힘이 전진 힘보다 작다.",
              "레귤레이터 압력을 올리니까 힘이 세졌지? F = P × A야. 하중보다 커야 밀 수 있어."),
            // ── 배관 ──
            Q("q.pipe.elbow", ConceptTag.PipeFitting, 1,
              "배관의 방향을 90°로 바꿀 때 사용하는 관 이음쇠는?",
              new[] { "엘보", "유니온", "니플", "플러그" }, 0,
              "엘보는 관의 방향을 바꾸는 이음쇠이며 90°와 45° 등이 있다.",
              "꺾이는 조각, 방금 계속 돌렸던 그거! 엘보라고 해."),
            Q("q.pipe.tee", ConceptTag.PipeFitting, 1,
              "배관 도중에서 관을 분기할 때 사용하는 관 이음쇠는?",
              new[] { "엘보", "티(T)", "소켓", "캡" }, 1,
              "티는 T자 모양으로 관을 세 방향으로 나눌 때 쓴다.",
              "세 갈래 조각이 새는 곳을 만들기 쉬웠지? 분기용 티야."),
            Q("q.pipe.union", ConceptTag.PipeFitting, 2,
              "배관을 쉽게 분해·조립할 수 있도록 관 중간에 사용하는 이음쇠는?",
              new[] { "유니온", "엘보", "티", "플러그" }, 0,
              "유니온은 나사를 풀어 관을 분리할 수 있어 보수가 필요한 곳에 쓴다.",
              "고칠 일이 잦은 자리엔 유니온을 달아 두는 거야. 다음 수리가 편해지거든."),

            // ── 유압 ──
            Q("q.hyd.pressure", ConceptTag.HydraulicPressure, 1,
              "압력(P), 힘(F), 면적(A)의 관계식으로 옳은 것은?",
              new[] { "P = F / A", "P = F × A", "P = A / F", "P = F + A" }, 0,
              "압력은 단위 면적당 작용하는 힘이다.",
              "작은 피스톤일수록 같은 힘에도 압력이 확 올라가. 압력계 바늘로 봤지?"),
            Q("q.hyd.pascal", ConceptTag.HydraulicPressure, 1,
              "밀폐된 용기 속 유체에 가한 압력이 모든 방향으로 같은 크기로 전달된다는 원리는?",
              new[] { "파스칼의 원리", "베르누이의 정리", "보일의 법칙", "아르키메데스의 원리" }, 0,
              "파스칼의 원리는 유압 잭, 유압 브레이크 등 유압 장치의 기본 원리다.",
              "작은 펌프로 거대한 하중을 든 게 바로 파스칼의 원리야."),
            Q("q.hyd.jack", ConceptTag.HydraulicPressure, 2,
              "단면적 10cm²인 입력 피스톤에 100N의 힘을 가했다. 단면적 100cm²인 출력 피스톤이 내는 힘은? (손실 무시)",
              new[] { "100N", "500N", "1000N", "10000N" }, 2,
              "P = 100N / 10cm² = 10N/cm². 출력 힘 = 10N/cm² × 100cm² = 1000N. 힘은 면적비(10배)만큼 커진다.",
              "면적비가 곧 힘의 배율이야. 크레인 설계할 때 계산한 그대로지."),
            Q("q.hyd.relief", ConceptTag.HydraulicPressure, 2,
              "유압 회로의 최고 압력을 제한하여 기기와 배관을 보호하는 밸브는?",
              new[] { "릴리프 밸브", "체크 밸브", "유량조절 밸브", "셔틀 밸브" }, 0,
              "릴리프 밸브는 설정 압력을 넘으면 기름을 탱크로 돌려보내 회로 압력을 제한한다.",
              "하중이 너무 무거울 때 '푸슉' 하고 열린 게 릴리프 밸브야. 호스가 터지지 않게 지켜 준 거지."),
            Q("q.hyd.tradeoff", ConceptTag.HydraulicPressure, 3,
              "유압 잭이 작은 힘으로 큰 하중을 들어 올릴 수 있는 대신 감수해야 하는 것은?",
              new[] { "출력 피스톤의 이동 거리가 짧아진다", "유체의 온도가 내려간다", "압력이 낮아진다", "에너지가 늘어난다" }, 0,
              "일(힘×거리)은 보존되므로 힘이 면적비만큼 커지면 이동 거리는 같은 비율로 줄어든다.",
              "힘을 키울수록 펌프질을 더 많이 해야 했지? 힘은 이득, 거리는 손해야."),

            // ── PLC ──
            Q("q.plc.bcontact", ConceptTag.LadderSelfHold, 1,
              "평상시에는 닫혀 있다가 조작하면 열리는 접점은?",
              new[] { "a접점", "b접점", "c접점", "열동 접점" }, 1,
              "b접점(브레이크 접점)은 평상시 닫혀 있고 동작하면 열린다. a접점은 반대다.",
              "STOP에 b접점을 쓰니까 버튼을 누르는 순간 회로가 끊겨서 멈췄지?"),
            Q("q.plc.selfhold", ConceptTag.LadderSelfHold, 1,
              "푸시버튼에서 손을 떼어도 출력이 유지되도록 출력 접점을 시작 접점에 병렬로 연결하는 회로는?",
              new[] { "자기유지 회로", "인터록 회로", "온 딜레이 회로", "일치 회로" }, 0,
              "출력 코일의 a접점을 START 접점에 병렬로 넣으면 한 번 ON된 출력이 스스로 유지된다.",
              "START에서 손을 뗐는데도 발전기가 돌았던 이유, 이제 알겠지?"),
            Q("q.plc.interlock", ConceptTag.LadderInterlock, 2,
              "전동기의 정회전·역회전 회로에서 두 출력이 동시에 ON 되지 않도록 서로 상대 출력의 b접점을 직렬로 넣는 회로는?",
              new[] { "인터록 회로", "자기유지 회로", "온 딜레이 회로", "카운터 회로" }, 0,
              "인터록(상호 배타) 회로는 한쪽이 동작 중이면 다른 쪽이 동작하지 못하게 막아 단락 사고를 방지한다.",
              "정·역을 동시에 누르면 합선 나던 거 기억나? 그걸 막는 게 인터록이야."),
            Q("q.plc.ton", ConceptTag.LadderTimer, 2,
              "입력 조건이 ON 되고 설정 시간이 지난 뒤에 출력이 ON 되는 타이머는?",
              new[] { "온 딜레이 타이머(TON)", "오프 딜레이 타이머(TOF)", "업 카운터", "원숏 타이머" }, 0,
              "온 딜레이 타이머는 입력이 설정 시간 동안 계속 ON일 때 출력이 켜진다.",
              "경광등이 먼저 켜지고 3초 뒤에 컨베이어가 돌았지? 그게 온 딜레이야."),
            Q("q.plc.ton.reset", ConceptTag.LadderTimer, 3,
              "온 딜레이 타이머의 입력이 설정 시간 도중에 OFF 되면 경과 시간은 어떻게 되는가?",
              new[] { "0으로 리셋된다", "그대로 유지된다", "설정값으로 바뀐다", "두 배로 늘어난다" }, 0,
              "일반 TON은 입력이 끊기면 경과 시간이 0으로 초기화되어 다시 처음부터 센다.",
              "경보 중에 STOP했다가 다시 눌렀을 때 3초를 처음부터 기다렸지? 그게 리셋이야."),

            // ── 센서 · 안전 ──
            Q("q.sensor.inductive", ConceptTag.Sensor, 1,
              "금속체만 검출할 수 있는 비접촉 센서는?",
              new[] { "유도형 근접센서", "정전용량형 근접센서", "광전센서", "초음파센서" }, 0,
              "유도형 근접센서는 금속체가 가까워질 때의 유도 전류 변화로 검출하므로 금속만 감지한다.",
              "컨베이어 위 금속 상자만 감지하던 센서가 유도형이야."),
            Q("q.safety.loto", ConceptTag.Safety, 1,
              "설비 정비 중 다른 사람이 실수로 전원을 넣지 못하도록 스위치를 잠그고 표지를 거는 안전조치는?",
              new[] { "잠금·표지(LOTO)", "접지", "절연", "누전 차단" }, 0,
              "잠금·표지(Lock-out/Tag-out)는 정비 중 에너지 투입을 물리적으로 막는 안전조치다.",
              "수리하는 동안엔 누가 전원을 넣지 못하게 자물쇠부터 걸자. 이 행성엔 나 혼자지만, 습관이야."),
            Q("q.safety.residual", ConceptTag.Safety, 2,
              "공압 배관을 분해하기 전에 가장 먼저 해야 할 안전조치는?",
              new[] { "공급을 차단하고 잔압을 제거한다", "밸브를 최대로 연다", "윤활유를 주입한다", "압력을 올려 누설을 확인한다" }, 0,
              "배관 안에 남은 압력이 분해 중 갑자기 방출되면 부상 위험이 크므로 공급 차단 후 잔압을 먼저 뺀다.",
              "압력이 남은 배관을 풀면 부품이 튀어. 잔압 빼는 습관, 잊지 마."),
        };

        private static PuzzleScenarioEntry E(string id, PuzzleKind kind, ConceptTag concept, int minTier, int maxTier,
                                             string intro, params string[] questionIds) =>
            new PuzzleScenarioEntry
            {
                id = id, kind = kind, concept = concept, minTier = minTier, maxTier = maxTier,
                introLine = intro, questionIds = new List<string>(questionIds)
            };

        public static List<PuzzleScenarioEntry> Entries()
        {
            var list = new List<PuzzleScenarioEntry>
            {
                // 배관/공압
                E("pipe.basic", PuzzleKind.Pipe, ConceptTag.PipeFitting, 1, 2,
                  "정수기 배관이 새고 있어. 압력원에서 피스톤까지 공기가 안 새게 이어 봐.",
                  "q.pipe.elbow", "q.pipe.tee", "q.safety.residual"),
                E("pipe.large", PuzzleKind.Pipe, ConceptTag.PipeFitting, 3, 5,
                  "배관망이 커졌어. 막힌 길을 피해 돌아가는 경로를 찾아야 해.",
                  "q.pipe.union", "q.pipe.tee", "q.safety.residual"),
                E("pipe.cylinder", PuzzleKind.Pipe, ConceptTag.PneumaticCylinder, 2, 5,
                  "실린더가 안 움직여. 압축공기가 끝까지 닿는지 확인하자.",
                  "q.pneu.cyl.single", "q.pneu.valve.52", "q.pneu.frl"),

                // 래더 (티어 1~5)
                E("ladder.selfhold", PuzzleKind.Ladder, ConceptTag.LadderSelfHold, 1, 2,
                  "제어반이 타서 접점이 빠졌어. 손을 떼도 계속 도는 회로를 만들어 줘.",
                  "q.plc.selfhold", "q.plc.bcontact", "q.safety.loto"),
                E("ladder.interlock", PuzzleKind.Ladder, ConceptTag.LadderInterlock, 3, 3,
                  "컨베이어 정·역 회로야. 동시에 켜지면 모터가 타 버려!",
                  "q.plc.interlock", "q.plc.bcontact"),
                E("ladder.ton", PuzzleKind.Ladder, ConceptTag.LadderTimer, 4, 4,
                  "경보부터 울리고 3초 뒤에 기동해야 해. 타이머를 써 보자.",
                  "q.plc.ton", "q.plc.ton.reset", "q.sensor.inductive"),
                E("ladder.fwdrev", PuzzleKind.Ladder, ConceptTag.LadderInterlock, 5, 5,
                  "윈치 제어반 최종 복구야. 인터락과 타이머를 한꺼번에 써야 해.",
                  "q.plc.interlock", "q.plc.ton.reset"),

                // 유압
                E("hydraulic.lv1", PuzzleKind.Hydraulic, ConceptTag.HydraulicPressure, 1, 2,
                  "소형 크레인으로 화물을 들어 올려야 해. 피스톤 크기를 골라 보자.",
                  "q.hyd.pressure", "q.hyd.pascal", "q.hyd.jack"),
                E("hydraulic.lv2", PuzzleKind.Hydraulic, ConceptTag.HydraulicPressure, 3, 4,
                  "바위를 옮겨야 해. 호스 한계와 릴리프 설정까지 챙겨야 해.",
                  "q.hyd.jack", "q.hyd.relief", "q.hyd.tradeoff"),
                E("hydraulic.lv3", PuzzleKind.Hydraulic, ConceptTag.HydraulicPressure, 5, 5,
                  "가라앉은 컨테이너를 인양할 거야. 힘과 속도, 둘 다 잡아야 해.",
                  "q.hyd.relief", "q.hyd.tradeoff"),

                // 공압 회로 (5/2 밸브 + 복동 실린더)
                E("pneumatic.lv1", PuzzleKind.Pneumatic, ConceptTag.PneumaticCylinder, 1, 2,
                  "수확 암이 안 움직여. 밸브 출구를 실린더 양쪽에 알맞게 이어 줘.",
                  "q.pneu.cyl.double", "q.pneu.cyl.single", "q.pneu.valve.52"),
                E("pneumatic.lv2", PuzzleKind.Pneumatic, ConceptTag.PneumaticValve, 3, 4,
                  "공급 라인이 끊기고 소음기까지 막혔어. 정비하고 압력도 맞춰 보자.",
                  "q.pneu.muffler", "q.pneu.pressure", "q.pneu.frl"),
                E("pneumatic.lv3", PuzzleKind.Pneumatic, ConceptTag.PneumaticValve, 5, 5,
                  "메모리형 밸브야. 신호는 한 번만 줘도 돼. 대신 압력을 정확히 맞춰 줘.",
                  "q.pneu.bistable", "q.pneu.pressure", "q.pneu.muffler"),
            };

            // 퍼즐 종류별 파라미터 연결
            Set(list, "pipe.basic", e => e.pipeGridSize = 4);
            Set(list, "pipe.large", e => e.pipeGridSize = 6);
            Set(list, "pipe.cylinder", e => e.pipeGridSize = 5);
            Set(list, "ladder.selfhold", e => e.ladderChallengeId = LadderChallengeLibrary.SelfHold);
            Set(list, "ladder.interlock", e => e.ladderChallengeId = LadderChallengeLibrary.Interlock);
            Set(list, "ladder.ton", e => e.ladderChallengeId = LadderChallengeLibrary.TonAlarm);
            Set(list, "ladder.fwdrev", e => e.ladderChallengeId = LadderChallengeLibrary.FwdRevTimer);
            Set(list, "hydraulic.lv1", e => e.hydraulicLevelId = HydraulicLevelLibrary.Lv1);
            Set(list, "hydraulic.lv2", e => e.hydraulicLevelId = HydraulicLevelLibrary.Lv2);
            Set(list, "hydraulic.lv3", e => e.hydraulicLevelId = HydraulicLevelLibrary.Lv3);
            Set(list, "pneumatic.lv1", e => e.pneumaticLevelId = PneumaticLevelLibrary.Lv1);
            Set(list, "pneumatic.lv2", e => e.pneumaticLevelId = PneumaticLevelLibrary.Lv2);
            Set(list, "pneumatic.lv3", e => e.pneumaticLevelId = PneumaticLevelLibrary.Lv3);
            return list;
        }

        private static void Set(List<PuzzleScenarioEntry> list, string id, System.Action<PuzzleScenarioEntry> apply)
        {
            var e = list.Find(x => x.id == id);
            if (e != null) apply(e);
        }
    }
}
