using System;

namespace AutoSurvival.UI
{
    /// <summary>
    /// 보기 순서 섞기 (UnityEngine 의존 없음 -> 테스트 가능).
    /// 같은 문제를 다시 만나도 '항상 1번이 정답' 같은 위치 암기를 막는다.
    /// 화면 번호(shuffled)와 원래 번호(original)를 오가는 매핑을 함께 돌려주어, 정오답 기록은 항상 원래 번호로 한다.
    /// </summary>
    public sealed class ShuffledChoices
    {
        public string[] choices;          // 화면에 보일 순서
        public int[] originalIndex;       // 화면 i번째 보기의 원래 번호
        public int answerIndex;           // 화면 기준 정답 번호

        public int ToOriginal(int shuffledIndex) => originalIndex[shuffledIndex];
    }

    public static class ChoiceShuffler
    {
        /// <summary>Fisher-Yates. rng는 0~1 미만 난수 공급자(테스트용 주입).</summary>
        public static ShuffledChoices Shuffle(string[] choices, int answerIndex, Func<float> rng)
        {
            int n = choices.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            for (int i = n - 1; i > 0; i--)
            {
                int j = Math.Min(i, Math.Max(0, (int)(rng() * (i + 1))));
                (order[i], order[j]) = (order[j], order[i]);
            }

            var result = new ShuffledChoices { choices = new string[n], originalIndex = order, answerIndex = -1 };
            for (int i = 0; i < n; i++)
            {
                result.choices[i] = choices[order[i]];
                if (order[i] == answerIndex) result.answerIndex = i;
            }
            return result;
        }
    }
}
