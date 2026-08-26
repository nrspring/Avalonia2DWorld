namespace NWorld.WorldGeneration.App.Services;

internal static class SimplexNoise
{
    private static readonly int[][] Gradients =
    [
        [1, 1],
        [-1, 1],
        [1, -1],
        [-1, -1],
        [1, 0],
        [-1, 0],
        [1, 0],
        [-1, 0],
        [0, 1],
        [0, -1],
        [0, 1],
        [0, -1],
    ];

    private static readonly int[] Permutation =
    [
        151, 160, 137, 91, 90, 15,
        131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
        140, 36, 103, 30, 69, 142, 8, 99, 37, 240,
        21, 10, 23, 190, 6, 148, 247, 120, 234, 75,
        0, 26, 197, 62, 94, 252, 219, 203, 117, 35,
        11, 32, 57, 177, 33, 88, 237, 149, 56, 87,
        174, 20, 125, 136, 171, 168, 68, 175, 74, 165,
        71, 134, 139, 48, 27, 166, 77, 146, 158, 231,
        83, 111, 229, 122, 60, 211, 133, 230, 220, 105,
        92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
        65, 25, 63, 161, 1, 216, 80, 73, 209, 76,
        132, 187, 208, 89, 18, 169, 200, 196, 135, 130,
        116, 188, 159, 86, 164, 100, 109, 198, 173, 186,
        3, 64, 52, 217, 226, 250, 124, 123, 5, 202,
        38, 147, 118, 126, 255, 82, 85, 212, 207, 206,
        59, 227, 47, 16, 58, 17, 182, 189, 28, 42,
        223, 183, 170, 213, 119, 248, 152, 2, 44, 154,
        163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
        129, 22, 39, 253, 19, 98, 108, 110, 79, 113,
        224, 232, 178, 185, 112, 104, 218, 246, 97, 228,
        251, 34, 242, 193, 238, 210, 144, 12, 191, 179,
        162, 241, 81, 51, 145, 235, 249, 14, 239, 107,
        49, 192, 214, 31, 181, 199, 106, 157, 184, 84,
        204, 176, 115, 121, 50, 45, 127, 4, 150, 254,
        138, 236, 205, 93, 222, 114, 67, 29, 24, 72,
        243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
    ];

    private static readonly int[] Perm = BuildPermutationTable();

    public static float Noise(float xin, float yin)
    {
        const float skewFactor = 0.3660254037844386f;
        const float unskewFactor = 0.21132486540518713f;

        var skew = (xin + yin) * skewFactor;
        var cellX = FastFloor(xin + skew);
        var cellY = FastFloor(yin + skew);
        var unskew = (cellX + cellY) * unskewFactor;
        var x0 = xin - (cellX - unskew);
        var y0 = yin - (cellY - unskew);

        int i1;
        int j1;

        if (x0 > y0)
        {
            i1 = 1;
            j1 = 0;
        }
        else
        {
            i1 = 0;
            j1 = 1;
        }

        var x1 = x0 - i1 + unskewFactor;
        var y1 = y0 - j1 + unskewFactor;
        var x2 = x0 - 1f + (2f * unskewFactor);
        var y2 = y0 - 1f + (2f * unskewFactor);
        var ii = cellX & 255;
        var jj = cellY & 255;
        var gi0 = Perm[ii + Perm[jj]] % 12;
        var gi1 = Perm[ii + i1 + Perm[jj + j1]] % 12;
        var gi2 = Perm[ii + 1 + Perm[jj + 1]] % 12;
        var n0 = CornerContribution(gi0, x0, y0);
        var n1 = CornerContribution(gi1, x1, y1);
        var n2 = CornerContribution(gi2, x2, y2);

        return 70f * (n0 + n1 + n2);
    }

    private static float CornerContribution(int gradientIndex, float x, float y)
    {
        var t = 0.5f - (x * x) - (y * y);

        if (t < 0f)
        {
            return 0f;
        }

        t *= t;
        return t * t * Dot(Gradients[gradientIndex], x, y);
    }

    private static int[] BuildPermutationTable()
    {
        var table = new int[512];

        for (var i = 0; i < table.Length; i++)
        {
            table[i] = Permutation[i & 255];
        }

        return table;
    }

    private static int FastFloor(float value)
    {
        var truncated = (int)value;
        return value < truncated ? truncated - 1 : truncated;
    }

    private static float Dot(IReadOnlyList<int> gradient, float x, float y)
    {
        return (gradient[0] * x) + (gradient[1] * y);
    }
}
