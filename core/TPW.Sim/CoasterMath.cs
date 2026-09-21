using System;
using System.Numerics;

namespace TPW.Sim
{
    /// <summary>READ: PSX integer spline and geometry arithmetic. Tables are literal PAL data;
    /// tools/coaster_source.py verifies every entry against TPW.BIN. No floating-point replacement.</summary>
    public static class CoasterMath
    {
        public const int One = 4096; // READ: 0x800BF2C4.
        public const int Tension = -2048; // READ: signed word 0x80103558, read at 0x800BF2C0.

        public static int Fraction(int distance, short length)
            => length == 0 ? 0 : unchecked((One / length) * distance) >> 8; // 0x800B2150..218C.

        /// <summary>READ: SDK SquareRoot0, 0x800CD87C..8FC. The lookup quantizes the mantissa;
        /// this is NOT floor(sqrt(n)). Negative wrapped squared lengths are outside this port.</summary>
        public static int SquareRoot(int squared)
        {
            if (squared < 0) throw new ArgumentOutOfRangeException(nameof(squared));
            if (squared == 0) return 0;
            int leading = BitOperations.LeadingZeroCount((uint)squared) & ~1;
            int exponent = (31 - leading) >> 1;
            int mantissa = leading >= 24 ? squared << (leading - 24) : squared >> (24 - leading);
            return (int)((uint)(Root[mantissa - 64] << exponent) >> 12);
        }

        /// <summary>READ: VectorNormal, 0x800C91A8..260; GTE inputs narrow to signed halfwords.
        /// GUESS-high: return zero for a degenerate tangent; original table underflow is undefined.
        /// A live degenerate-route trace would settle that exceptional case.</summary>
        public static CoasterVector Normalize(CoasterVector vector)
        {
            int x = unchecked((short)vector.X), y = unchecked((short)vector.Y), z = unchecked((short)vector.Z);
            int squared = unchecked(x * x + y * y + z * z);
            if (squared == 0) return default;
            if (squared < 0) throw new ArgumentOutOfRangeException(nameof(vector));
            int leading = BitOperations.LeadingZeroCount((uint)squared) & ~1;
            int exponent = (31 - leading) >> 1;
            int mantissa = leading >= 24 ? squared << (leading - 24) : squared >> (24 - leading);
            int factor = Normal[mantissa - 64];
            return new CoasterVector(x * factor >> exponent, y * factor >> exponent, z * factor >> exponent);
        }

        /// <summary>READ: 0x800BF2A8..5B4. Logical shifts, low-word products, signed halfword
        /// coefficients, and separate rounding of the fourth control point are intentional.</summary>
        public static CoasterSample Spline(CoasterVector p0, CoasterVector p1, CoasterVector p2, CoasterVector p3, int t)
        {
            unchecked
            {
                int t2 = (int)((uint)(t * t) >> 12);
                int t3 = (int)((uint)(t2 * t) >> 12);
                int k = (One - Tension) >> 1;
                short a = (short)((uint)(k * (-t3 + 2 * t2 - t)) >> 12);
                short b = (short)((k * (-t3 + t2) >> 12) + 2 * t3 - 3 * t2 + One);
                short c = (short)(((uint)(k * (t3 - 2 * t2 + t)) >> 12) - 2 * t3 + 3 * t2);
                short d = (short)(k * (t3 - t2) >> 12);
                var position = Weighted(p0, p1, p2, p3, a, b, c, d);
                a = (short)((uint)(k * (-3 * t2 + 4 * t - One)) >> 12);
                b = (short)(((uint)(k * (-3 * t2 + 2 * t)) >> 12) + 6 * t2 - 6 * t);
                c = (short)(((uint)(k * (3 * t2 - 4 * t + One)) >> 12) - 6 * t2 + 6 * t);
                d = (short)((uint)(k * (3 * t2 - 2 * t)) >> 12);
                var tangent = Normalize(Weighted(p0, p1, p2, p3, a, b, c, d));
                return new CoasterSample(position, tangent, 0);
            }
        }

        static CoasterVector Weighted(CoasterVector p0, CoasterVector p1, CoasterVector p2, CoasterVector p3,
            short a, short b, short c, short d)
        {
            // READ: three-column GTE MVMVA (0x800BF3B8), fourth coefficient via mflo/>>12.
            static int Axis(int x0, int x1, int x2, int x3, int a, int b, int c, int d)
                => unchecked((int)(((long)(short)x0 * a + (long)(short)x1 * b + (long)(short)x2 * c) >> 12)
                    + (x3 * d >> 12));
            return new CoasterVector(Axis(p0.X,p1.X,p2.X,p3.X,a,b,c,d),
                Axis(p0.Y,p1.Y,p2.Y,p3.Y,a,b,c,d), Axis(p0.Z,p1.Z,p2.Z,p3.Z,a,b,c,d));
        }

        // READ: 192 signed halfwords each, 0x801018D0 and 0x800F97B4.
        static readonly short[] Root = {
            4096,4127,4159,4190,4222,4252,4283,4314,4344,4374,4404,4434,4463,4492,4521,4550,
            4579,4608,4636,4664,4692,4720,4748,4775,4802,4830,4857,4884,4910,4937,4964,4990,
            5016,5042,5068,5094,5120,5145,5170,5196,5221,5246,5271,5296,5320,5345,5369,5394,
            5418,5442,5466,5490,5514,5538,5561,5585,5608,5632,5655,5678,5701,5724,5747,5769,
            5792,5815,5837,5860,5882,5904,5926,5948,5970,5992,6014,6036,6058,6079,6101,6122,
            6144,6165,6186,6207,6228,6249,6270,6291,6312,6333,6353,6374,6394,6415,6435,6456,
            6476,6496,6516,6536,6556,6576,6596,6616,6636,6656,6675,6695,6714,6734,6753,6773,
            6792,6811,6830,6850,6869,6888,6907,6926,6945,6963,6982,7001,7020,7038,7057,7075,
            7094,7112,7131,7149,7168,7186,7204,7222,7240,7258,7276,7294,7312,7330,7348,7366,
            7384,7401,7419,7437,7454,7472,7489,7507,7524,7542,7559,7576,7594,7611,7628,7645,
            7662,7680,7697,7714,7731,7747,7764,7781,7798,7815,7832,7848,7865,7882,7898,7915,
            7931,7948,7964,7981,7997,8014,8030,8046,8062,8079,8095,8111,8127,8143,8159,8175
        };
        static readonly short[] Normal = {
            4096,4064,4033,4003,3973,3944,3916,3888,3861,3835,3809,3783,3758,3734,3710,3686,
            3663,3640,3618,3596,3575,3554,3533,3513,3493,3473,3454,3435,3416,3397,3379,3361,
            3344,3327,3310,3293,3276,3260,3244,3228,3213,3197,3182,3167,3153,3138,3124,3110,
            3096,3082,3069,3055,3042,3029,3016,3003,2991,2978,2966,2954,2942,2930,2919,2907,
            2896,2885,2873,2862,2852,2841,2830,2820,2809,2799,2789,2779,2769,2759,2749,2740,
            2730,2721,2711,2702,2693,2684,2675,2666,2657,2649,2640,2631,2623,2615,2606,2598,
            2590,2582,2574,2566,2558,2550,2543,2535,2528,2520,2513,2505,2498,2491,2484,2477,
            2469,2462,2456,2449,2442,2435,2428,2422,2415,2409,2402,2396,2389,2383,2377,2371,
            2364,2358,2352,2346,2340,2334,2328,2322,2317,2311,2305,2299,2294,2288,2283,2277,
            2272,2266,2261,2255,2250,2245,2239,2234,2229,2224,2219,2214,2209,2204,2199,2194,
            2189,2184,2179,2174,2170,2165,2160,2155,2151,2146,2142,2137,2133,2128,2124,2119,
            2115,2110,2106,2102,2097,2093,2089,2084,2080,2076,2072,2068,2064,2060,2056,2052
        };
    }
}
