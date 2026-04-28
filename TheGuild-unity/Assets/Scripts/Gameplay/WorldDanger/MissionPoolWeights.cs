namespace TheGuild.Gameplay.WorldDanger
{
    /// <summary>
    /// 任務池權重 DTO。
    /// </summary>
    public readonly struct MissionPoolWeights
    {
        public MissionPoolWeights(int weightF_E, int weightD, int weightC, int weightB, int weightA, int weightS_SSS)
        {
            this.weightF_E = weightF_E;
            this.weightD = weightD;
            this.weightC = weightC;
            this.weightB = weightB;
            this.weightA = weightA;
            this.weightS_SSS = weightS_SSS;
        }

        public readonly int weightF_E;
        public readonly int weightD;
        public readonly int weightC;
        public readonly int weightB;
        public readonly int weightA;
        public readonly int weightS_SSS;

        public bool IsAllZero
        {
            get { return weightF_E == 0 && weightD == 0 && weightC == 0 && weightB == 0 && weightA == 0 && weightS_SSS == 0; }
        }
    }
}
