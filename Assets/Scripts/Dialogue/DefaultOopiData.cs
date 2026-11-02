using System.Collections.Generic;
using AutoSurvival.Core;
using S = AutoSurvival.Dialogue.OopiSituation;
using P = AutoSurvival.Dialogue.OopiPuzzleFilter;
using M = AutoSurvival.Dialogue.OopiMood;

namespace AutoSurvival.Dialogue
{
    /// <summary>
    /// 오피의 기본 대사 샘플. 말투: 반말, 정비사 동료 느낌, 기계 농담 + 기능사 지식 한 줄(tip).
    /// 정답을 직접 알려 주지 않고 '어긋난 상황'과 '생각할 방향'만 준다.
    /// 자리표시자: {player} {machine} {tier} {nexttier} {concept} {seconds} {island} {percent} {item}
    /// </summary>
    public static class DefaultOopiData
    {
        private static OopiLine L(string id, S situation, P puzzle, string cause, M mood, string text, string tip = "") =>
            new OopiLine { id = id, situation = situation, puzzle = puzzle, cause = cause ?? "", mood = mood, text = text, tip = tip };

        private static OopiLine Tiers(this OopiLine l, int min, int max) { l.minTier = min; l.maxTier = max; return l; }
        private static OopiLine ForConcept(this OopiLine l, ConceptTag c) { l.restrictConcept = true; l.concept = c; return l; }
        private static OopiLine Once(this OopiLine l) { l.once = true; return l; }

        public static List<OopiLine> Lines() => new()
        {
            // ───────── 퍼즐 진입 ─────────
            L("pipe.enter.1", S.PuzzleEnter, P.Pipe, null, M.Neutral,
              "{machine} 배관이야. 압력원에서 피스톤까지 길이 끊기지 않아야 해. 새는 곳이 있으면 그게 다 손실이거든.",
              "배관 이음쇠: 엘보는 방향 전환, 티는 분기, 유니온은 분해·조립이 쉬운 연결."),
            L("pipe.enter.2", S.PuzzleEnter, P.Pipe, null, M.Joke,
              "배관은 사람 혈관이랑 똑같아. 막히면 짜증 내고, 새면 힘이 빠지지. 자, 청진기 들어."),
            L("pipe.enter.3", S.PuzzleEnter, P.Pipe, null, M.Neutral,
              "배관망이 꽤 커졌네. 막다른 길은 과감히 포기하고 돌아가자. 후진도 전진의 일부야.").Tiers(3, 5),

            L("ladder.enter.1", S.PuzzleEnter, P.Ladder, null, M.Neutral,
              "제어반 뚜껑 열었다. a접점은 평소 열려 있고, b접점은 평소 닫혀 있어. 헷갈리면 이것부터 떠올려.",
              "a접점(NO)은 신호가 오면 연결, b접점(NC)은 신호가 오면 끊어진다."),
            L("ladder.enter.2", S.PuzzleEnter, P.Ladder, null, M.Joke,
              "접점 하나 빠졌다고 모터가 파업이야. 노조가 강하네, 이 제어반."),
            L("ladder.enter.3", S.PuzzleEnter, P.Ladder, null, M.Serious,
              "정·역 회로야. 둘이 동시에 켜지면 모터가 줄다리기하다 타 버려. 서로를 막아 주는 장치를 생각해 봐.",
              "인터록은 한쪽이 동작하면 반대쪽이 동작하지 못하게 하는 안전 회로다.").ForConcept(ConceptTag.LadderInterlock),
            L("ladder.enter.4", S.PuzzleEnter, P.Ladder, null, M.Neutral,
              "타이머 회로야. 입력이 들어오고 나서 설정 시간이 지나야 켜지는 게 TON이지. 중간에 입력이 끊기면? 처음부터 다시 세.",
              "TON(온 딜레이): 입력 ON 후 설정 시간이 지나면 출력 ON, 입력이 끊기면 리셋.").ForConcept(ConceptTag.LadderTimer),
            L("ladder.enter.5", S.PuzzleEnter, P.Ladder, null, M.Cheer,
              "START에서 손을 떼도 계속 도는 회로를 만들어야 해. 회로가 '기억'을 가져야 한다는 뜻이지.",
              "자기유지 회로는 시퀀스 제어의 가장 기본이 되는 회로다.").ForConcept(ConceptTag.LadderSelfHold),

            L("hyd.enter.1", S.PuzzleEnter, P.Hydraulic, null, M.Cheer,
              "유압 크레인이야. 작은 힘으로 큰 하중을, 파스칼의 원리가 오늘의 주인공이지.",
              "파스칼의 원리: 밀폐된 유체에 가한 압력은 모든 방향으로 같게 전달된다. P = F ÷ A"),
            L("hyd.enter.2", S.PuzzleEnter, P.Hydraulic, null, M.Joke,
              "기름은 거의 안 줄어들어서 정직해. 공압처럼 '탄성' 있는 척 안 한다고.",
              "유압유는 비압축성에 가까워 힘 전달이 정확하다 (공압은 압축성)."),
            L("hyd.enter.3", S.PuzzleEnter, P.Hydraulic, null, M.Serious,
              "릴리프 밸브 설정 잊지 마. 안전장치는 호스보다 약해야 안전장치 구실을 하지.",
              "릴리프 밸브: 회로의 최고압력을 제한한다. 호스 허용압보다 낮게 설정한다.").Tiers(3, 5),

            L("pneu.enter.1", S.PuzzleEnter, P.Pneumatic, null, M.Neutral,
              "공압 실린더야. 5/2 밸브의 출구 2번과 4번을 실린더 양쪽에 이어 주면 돼. 어느 쪽이 전진인지는 밸브가 아니라 네 호스가 정해.",
              "5/2 밸브 포트: 1=공급, 2·4=출구, 3·5=배기."),
            L("pneu.enter.2", S.PuzzleEnter, P.Pneumatic, null, M.Joke,
              "공기는 공짜지만 컴프레서는 아니라는 거, 잊지 마. 새는 공기 한 줄기가 전기세 한 줄이야."),
            L("pneu.enter.3", S.PuzzleEnter, P.Pneumatic, null, M.Cheer,
              "메모리형 밸브야. 신호를 한 번만 줘도 위치를 기억해. 기억력 좋은 밸브라니, 어지간한 동료보다 낫네.",
              "양측 전자(파일럿) 밸브는 마지막에 받은 신호의 위치를 유지한다.").Tiers(5, 5),

            // ───────── 시간 촉박 ─────────
            L("time.pipe", S.TimeLow, P.Pipe, null, M.Worry, "압력 새는 소리가 점점 커져. {seconds}초밖에 안 남았어!"),
            L("time.ladder", S.TimeLow, P.Ladder, null, M.Worry, "PLC 스캔은 눈 깜짝할 새인데 우리는 느리네. {seconds}초 남았어!"),
            L("time.hyd", S.TimeLow, P.Hydraulic, null, M.Worry, "펌프 박자 놓치지 마! {seconds}초!"),
            L("time.pneu", S.TimeLow, P.Pneumatic, null, M.Worry, "컴프레서가 헉헉대고 있어. {seconds}초 안에 끝내자!"),
            L("time.any.1", S.TimeLow, P.Any, null, M.Serious, "배고프고 추우면 손끝이 둔해져. {seconds}초 안에 마무리하자."),
            L("time.any.2", S.TimeLow, P.Any, null, M.Joke, "{seconds}초! 숨 쉬는 것도 아끼자. 아, 숨은 쉬어."),

            // ───────── 성공 ─────────
            L("ok.pipe", S.Success, P.Pipe, null, M.Cheer,
              "압력이 끝까지 닿았어! 깔끔한데? 배관공 자격증 주고 싶다.",
              "배관 연결 후에는 누설 점검(비눗물 등)까지 해야 마무리다."),
            L("ok.ladder", S.Success, P.Ladder, null, M.Cheer,
              "시험 가동 통과! 모든 상황에서 정상이야. 래더는 거짓말을 안 해, 위에서 아래로 정직하게 돌 뿐이지.",
              "PLC는 입력 읽기 → 프로그램 실행 → 출력 갱신을 반복한다 (스캔)."),
            L("ok.hyd", S.Success, P.Hydraulic, null, M.Cheer,
              "하중이 올라갔어! 힘 이득은 거리 손해와 맞바꾼 거라는 거, 펌프 누르면서 느꼈지?",
              "일의 원리: 힘을 n배 얻으면 이동 거리는 1/n배가 된다."),
            L("ok.pneu", S.Success, P.Pneumatic, null, M.Cheer,
              "실린더가 시원하게 왕복해! 이 '칙-' 소리가 공압의 맛이지.",
              "복동 실린더는 양쪽 챔버에 번갈아 공기를 넣어 전진·후진한다."),
            L("ok.any", S.Success, P.Any, null, M.Joke,
              "수리 완료… 라고 말하고 싶지만 마지막으로 볼트 한 번 더 조여 보자. 현장은 마무리가 반이야."),

            // ───────── 처음 수리 / 티어 업 ─────────
            L("repaired.1", S.Repaired, P.Any, null, M.Cheer, "{machine} 가동 시작! 이 소리 들려? 엔지니어한테는 클래식 음악이야."),
            L("repaired.2", S.Repaired, P.Any, null, M.Neutral, "{machine}가 다시 숨을 쉬어. 멈춘 기계를 살리는 건 언제나 기분 좋아."),
            L("repaired.3", S.Repaired, P.Any, null, M.Joke, "{machine} 복구 완료. 업무일지에 적어 두자: '이상 없음'은 아니고 '이상 해결'."),

            L("tier.2", S.TierUp, P.Any, null, M.Cheer, "Tier {tier} 달성! 고친 만큼 돌려주는 게 기계의 미덕이지.").Tiers(2, 2),
            L("tier.3", S.TierUp, P.Any, null, M.Cheer, "Tier {tier}! 이제 {machine}가 제법 일꾼 같은데?").Tiers(3, 3),
            L("tier.4", S.TierUp, P.Any, null, M.Neutral, "Tier {tier}. 여기까지 오면 기계가 네 손맛을 알아봐.").Tiers(4, 4),
            L("tier.5", S.TierUp, P.Any, null, M.Cheer,
              "최대 티어 도달! 정비 기록 만점이야. 시험장에서도 이만큼만 침착하면 돼.").Tiers(5, 5),
            L("tier.mid", S.TierUp, P.Any, null, M.Joke,
              "{machine} Tier {tier} 업그레이드 완료. 다음 목표는 Tier {nexttier}인데… 자원은 괜찮아?").Tiers(2, 4),

            // ───────── 실패: 시간 초과 / 일반 ─────────
            L("fail.pipe", S.Failure, P.Pipe, OopiCauses.Timeout, M.Worry, "시간 초과! 압력이 다 새 버렸어. 막힌 곳부터 먼저 보고 돌아가는 길을 찾자."),
            L("fail.ladder", S.Failure, P.Ladder, OopiCauses.Timeout, M.Worry, "시간 초과. 시험 가동은 상황 하나씩 머릿속으로 따라가 보고 당기자. 레버가 닳아."),
            L("fail.hyd", S.Failure, P.Hydraulic, OopiCauses.Timeout, M.Worry, "시간 초과! 압력 = 힘 ÷ 면적, 계산부터 빨리 끝내고 펌프를 잡자."),
            L("fail.pneu", S.Failure, P.Pneumatic, OopiCauses.Timeout, M.Worry, "시간 초과. 호스가 많을 땐 공급 → 출구 → 배기 순서로 점검하자."),
            L("fail.any.1", S.Failure, P.Any, null, M.Neutral, "아쉽다. 하지만 고장 난 기계한테 배우는 게 제일 빨라."),
            L("fail.any.2", S.Failure, P.Any, null, M.Joke, "실패도 데이터야. 로그 남겨 두고 다시 가자."),

            // ───────── 진단: 공압 ─────────
            L("dx.pneu.over.1", S.Diagnosis, P.Pneumatic, "pneu.OverPressure", M.Worry,
              "펑! 호스가 터졌어. 레귤레이터 설정이 호스 허용압을 넘었어. 압력은 높다고 좋은 게 아니야.",
              "공압 회로의 사용 압력은 보통 5~7bar 수준이고, 레귤레이터로 설정한다."),
            L("dx.pneu.over.2", S.Diagnosis, P.Pneumatic, "pneu.OverPressure", M.Joke,
              "호스한테도 사정이 있어. 너무 몰아붙이면 터지는 건 사람이나 호스나 똑같지."),
            L("dx.pneu.nosup.1", S.Diagnosis, P.Pneumatic, "pneu.NoSupply", M.Neutral,
              "공기가 안 오고 있어. 컴프레서에서 밸브 1번 포트까지 길이 이어져 있는지 봐.",
              "서비스 유닛은 공기 필터, 레귤레이터, 루브리케이터로 구성된다."),
            L("dx.pneu.nosup.2", S.Diagnosis, P.Pneumatic, "pneu.NoSupply", M.Joke,
              "밥을 안 주고 일을 시키면 안 되지. 밸브 1번 포트가 공급(P)이야. 거기부터 확인해."),
            L("dx.pneu.leak.1", S.Diagnosis, P.Pneumatic, "pneu.Leak", M.Worry,
              "쉬이익… 이 소리, 호스 끝이 열려 있다는 뜻이야. 압력이 걸리는 포트엔 호스가 꽂혀 있어야지."),
            L("dx.pneu.leak.2", S.Diagnosis, P.Pneumatic, "pneu.Leak", M.Joke,
              "공기가 새는 건 돈이 새는 거야. 컴프레서가 괜히 돌겠어?",
              "압축공기 누설은 공장 에너지 손실의 큰 원인 중 하나다."),
            L("dx.pneu.short.1", S.Diagnosis, P.Pneumatic, "pneu.ShortCircuit", M.Worry,
              "출구 두 개를 같은 챔버에 꽂았어. 한쪽으로 들어간 공기가 다른 쪽 출구를 타고 곧장 배기로 빠져나가."),
            L("dx.pneu.short.2", S.Diagnosis, P.Pneumatic, "pneu.ShortCircuit", M.Neutral,
              "2번과 4번은 서로 다른 챔버로 가야 해. 한 방에 둘을 몰아넣으면 실린더는 구경만 하게 돼."),
            L("dx.pneu.block.1", S.Diagnosis, P.Pneumatic, "pneu.ExhaustBlocked", M.Worry,
              "배기구가 막혔어! 빠져나갈 데 없는 공기가 쿠션이 돼서 피스톤이 끝까지 못 가.",
              "소음기(머플러)가 막히면 배기 저항이 커져 실린더가 느려지거나 멈춘다."),
            L("dx.pneu.block.2", S.Diagnosis, P.Pneumatic, "pneu.ExhaustBlocked", M.Joke,
              "입 막고 숨 내쉬어 봐. 답답하지? 소음기가 막힌 실린더 기분이 딱 그래."),
            L("dx.pneu.rev.1", S.Diagnosis, P.Pneumatic, "pneu.ReversedConnection", M.Neutral,
              "반대로 꽂았네. 눌렀는데 후진이라니 실린더가 반항기야. 헤드쪽에 압력이 걸려야 전진해."),
            L("dx.pneu.rev.2", S.Diagnosis, P.Pneumatic, "pneu.ReversedConnection", M.Joke,
              "2번과 4번을 서로 바꿔 봐. 전진이냐 후진이냐는 밸브가 아니라 호스가 정해."),
            L("dx.pneu.nc.1", S.Diagnosis, P.Pneumatic, "pneu.NotConnected", M.Neutral,
              "가압돼야 할 챔버에 호스가 없어. 공기가 갈 곳이 없으면 피스톤도 제자리지."),
            L("dx.pneu.nc.2", S.Diagnosis, P.Pneumatic, "pneu.NotConnected", M.Joke,
              "포트가 비어 있잖아. 밸브는 일했는데 실린더는 몰랐대."),
            L("dx.pneu.force.1", S.Diagnosis, P.Pneumatic, "pneu.InsufficientForce", M.Neutral,
              "힘이 모자라. F = P × A야. 압력을 올리거나 면적을 키워야 해. 단, 호스 허용압은 넘기지 말고.",
              "실린더 출력 F = P × A (압력 × 피스톤 단면적)."),
            L("dx.pneu.force.2", S.Diagnosis, P.Pneumatic, "pneu.InsufficientForce", M.Joke,
              "하중이 압력을 이기고 있어. 레귤레이터를 한 단계만 올려 봐. 딱 한 단계만."),

            // ───────── 진단: 유압 ─────────
            L("dx.hyd.unsafe.1", S.Diagnosis, P.Hydraulic, "hyd.UnsafeRelief", M.Serious,
              "그 릴리프 설정은 거부한다. 호스 허용압보다 높으면 안전장치가 아니라 장식품이야.",
              "릴리프 밸브는 회로의 최고압력을 제한하는 안전 밸브다."),
            L("dx.hyd.unsafe.2", S.Diagnosis, P.Hydraulic, "hyd.UnsafeRelief", M.Joke,
              "안전장치가 호스보다 튼튼하면 호스가 먼저 사표를 내."),
            L("dx.hyd.relief.1", S.Diagnosis, P.Hydraulic, "hyd.ReliefOpens", M.Worry,
              "푸슉! 릴리프 밸브가 열렸어. 하중을 들어 올릴 압력보다 릴리프 설정이 낮아서 기름이 도망가."),
            L("dx.hyd.relief.2", S.Diagnosis, P.Hydraulic, "hyd.ReliefOpens", M.Joke,
              "릴리프가 일도 하기 전에 퇴근했네. 설정압을 필요한 압력 위로 올려 줘. 단, 호스 허용압 아래에서."),
            L("dx.hyd.burst.1", S.Diagnosis, P.Hydraulic, "hyd.HoseBurst", M.Worry,
              "펑! 호스가 터졌어. 출력 피스톤이 작으면 같은 힘에도 압력이 커져. P = F ÷ A, 기억나지?"),
            L("dx.hyd.burst.2", S.Diagnosis, P.Hydraulic, "hyd.HoseBurst", M.Serious,
              "고압 기름은 피부를 뚫을 만큼 위험해. 현실에선 압력을 빼고 만져야 해. 오늘은 게임이라 다행이지.",
              "유압 장치를 분해하기 전에는 반드시 잔압을 제거한다."),
            L("dx.hyd.force.1", S.Diagnosis, P.Hydraulic, "hyd.InsufficientForce", M.Neutral,
              "펌프 압력이 부족해서 하중이 안 떨어져. 입력 피스톤 면적을 줄이면 같은 힘에서 압력이 올라가.",
              "파스칼의 원리: P = F ÷ A, 출력 F2 = P × A2."),
            L("dx.hyd.force.2", S.Diagnosis, P.Hydraulic, "hyd.InsufficientForce", M.Joke,
              "하중이 꿈쩍도 안 해. 크레인이 '저 오늘 쉬는 날인데요' 하는 것 같아."),
            L("dx.hyd.slow.1", S.Diagnosis, P.Hydraulic, "hyd.TooSlow", M.Neutral,
              "힘은 충분한데 너무 느려. 면적비가 크면 힘은 커지지만 그만큼 펌핑을 많이 해야 해.",
              "일의 원리: 힘에서 이득을 보면 이동 거리에서 손해를 본다."),
            L("dx.hyd.slow.2", S.Diagnosis, P.Hydraulic, "hyd.TooSlow", M.Joke,
              "힘을 얻으면 거리가 대가야. 세상에 공짜 점심은 없고, 유압에는 공짜 힘이 없어."),
            L("dx.hyd.heat.1", S.Diagnosis, P.Hydraulic, "hyd.Overheat", M.Worry,
              "펌프 과열! 면적비가 너무 컸어. 에너지는 보존돼. 힘을 얻은 만큼 움직임으로 값을 치르는 거지."),
            L("dx.hyd.heat.2", S.Diagnosis, P.Hydraulic, "hyd.Overheat", M.Joke,
              "펌프에서 김이 나잖아! 허용 횟수 안에 들어올 면적비로 다시 계산해 보자."),

            // ───────── 진단: 래더 ─────────
            L("dx.lad.inter.1", S.Diagnosis, P.Ladder, "ladder.Interlock", M.Worry,
              "합선이야! 정·역이 동시에 켜졌어. 서로를 막아 주는 안전 회로가 필요해.",
              "인터록은 한쪽이 동작하면 반대쪽이 동작하지 못하게 한다 (정·역 운전 회로의 기본)."),
            L("dx.lad.inter.2", S.Diagnosis, P.Ladder, "ladder.Interlock", M.Joke,
              "모터가 양쪽으로 동시에 돌려고 했어. 모터 입장에선 줄다리기 지옥이지."),
            L("dx.lad.unst.1", S.Diagnosis, P.Ladder, "ladder.Unstable", M.Worry,
              "출력이 깜빡거려! 코일이 켜지면 자기 접점이 자기를 끄고 있어. 스캔마다 켜졌다 꺼졌다 하는 발진이야."),
            L("dx.lad.unst.2", S.Diagnosis, P.Ladder, "ladder.Unstable", M.Joke,
              "스스로를 부정하는 회로네. 철학적이긴 한데 모터는 못 돌려."),
            L("dx.lad.on.1", S.Diagnosis, P.Ladder, "ladder.ShouldBeOn", M.Neutral,
              "켜져야 할 출력이 꺼져 있어. 조건 접점이 빠졌거나 a접점과 b접점이 뒤바뀐 건 아닌지 봐.",
              "a접점은 평소 열림, b접점은 평소 닫힘."),
            L("dx.lad.on.2", S.Diagnosis, P.Ladder, "ladder.ShouldBeOn", M.Neutral,
              "손을 떼면 꺼지는 회로는 기억이 없다는 뜻이야. 출력이 자기 상태를 스스로 기억하게 할 방법을 생각해 봐.").ForConcept(ConceptTag.LadderSelfHold),
            L("dx.lad.on.3", S.Diagnosis, P.Ladder, "ladder.ShouldBeOn", M.Neutral,
              "아직 안 켜졌어. 설정 시간을 다 채우기 전에 입력이 끊기면 TON은 처음부터 다시 세.").ForConcept(ConceptTag.LadderTimer),
            L("dx.lad.off.1", S.Diagnosis, P.Ladder, "ladder.ShouldBeOff", M.Neutral,
              "꺼져야 할 출력이 켜져 있어. 정지 신호가 b접점으로 들어갔는지, 기억 회로가 정지를 이기고 있진 않은지 확인해 봐.",
              "정지(STOP) 버튼은 보통 b접점으로 직렬 연결해 신호를 끊는다."),
            L("dx.lad.off.2", S.Diagnosis, P.Ladder, "ladder.ShouldBeOff", M.Joke,
              "꺼야 하는데 안 꺼져. 끈질긴 건 좋은데 정지 신호한테는 져 줘야지."),

            // ───────── 생존 경고 (cause = 수치 이름) ─────────
            L("surv.hunger", S.SurvivalWarning, P.Any, "hunger", M.Worry, "배에서 꼬르륵 소리가 나. 엔지니어도 연료가 필요해. 연료 없는 발전기랑 똑같다니까."),
            L("surv.thirst", S.SurvivalWarning, P.Any, "thirst", M.Worry, "목이 마르면 판단력부터 떨어져. 물부터 챙기고 일하자."),
            L("surv.warmth", S.SurvivalWarning, P.Any, "warmth", M.Worry, "몸이 식고 있어. 모닥불 근처에서 데우고 오자. 손이 떨리면 접점 하나도 못 꽂아."),
            L("surv.health", S.SurvivalWarning, P.Any, "health", M.Serious, "체력이 위험해! 지금은 수리보다 휴식이 우선이야. 고장 난 기계는 기다려 주지만 사람은 아니거든."),
            L("surv.stamina", S.SurvivalWarning, P.Any, "stamina", M.Neutral, "숨이 차네. 호흡부터 고르자. 펌프 박자는 숨 고른 뒤에!"),

            // ───────── 섬 ─────────
            L("island.arrive.1", S.IslandArrive, P.Any, null, M.Neutral, "{island} 도착! 여기도 멈춘 기계가 한가득이네. 일감이 넘쳐서 행복하다…는 건 거짓말이야."),
            L("island.arrive.2", S.IslandArrive, P.Any, null, M.Cheer, "{island}의 공기 냄새가 달라. 녹슨 쇠 냄새랑 기름 냄새, 좋은 현장 냄새지."),
            L("island.clear.1", S.IslandCleared, P.Any, null, M.Cheer, "{island}의 설비가 전부 돌아가기 시작했어! 이제 다음 섬으로 건너갈 수 있어. 항해 보급은 챙겼지?"),
            L("island.clear.2", S.IslandCleared, P.Any, null, M.Joke, "{island} 클리어! 이 정도면 현장 책임자 명함 파도 되겠어."),

            // ───────── 혼잣말 ─────────
            L("idle.1", S.Idle, P.Any, null, M.Joke, "기계 돌아가는 소리는 엔지니어의 자장가야."),
            L("idle.2", S.Idle, P.Any, null, M.Joke, "나는 AI라 배는 안 고픈데, 네 배 소리는 여기까지 들려."),
            L("idle.3", S.Idle, P.Any, null, M.Serious,
              "점검 전엔 안전 확인, 한 번만 더. 이게 현장에서 제일 오래 사는 법이야.",
              "정비 전에는 전원·공급 차단(LOTO)과 잔압 제거를 먼저 한다."),
            L("idle.4", S.Idle, P.Any, null, M.Neutral, "혹시 알아? 시험 문제는 우리가 방금 고친 그 기계에서 나온다는 거."),
        };
    }
}
