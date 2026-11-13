// The kernels of NumericBench.cs as C++ that an IL2CPP build compiles together with the code it generates: a .cpp
// below Assets is a source plugin, and C# calls it through [DllImport("__Internal")].
//
// A source plugin is in every IL2CPP Player the project builds, not only in the bench. So the whole file is empty
// unless the build defines GENERIC_MEMORY_NATIVE for the C++ compiler (il2cpp's --compiler-flags=-DGENERIC_MEMORY_NATIVE,
// which tools/il2cpp-generic-memory/e-android-build.sh passes), and NumericBench.cs imports these functions only
// when the scripting define of the same name is set.
//
// The loops are plain C++, so that what is measured is what the compiler vectorizes by itself with the flags of the
// build. The functions ending in _simd are written with NEON (arm_neon.h) on arm64, and are the plain loop elsewhere.
#if defined(GENERIC_MEMORY_NATIVE)

#include <math.h>
#include <stdint.h>
#include <string.h>

#if defined(__aarch64__)
#include <arm_neon.h>
#endif

#if defined(_MSC_VER)
#define GM_RESTRICT __restrict
#else
#define GM_RESTRICT __restrict__
#endif

extern "C"
{
    struct GmFloat3
    {
        float x, y, z;
    };

    int32_t gm_native_add_one(int32_t value)
    {
        return value + 1;
    }

    float gm_native_float_sum(const float* a, int32_t count)
    {
        float sum = 0.0f;
        for (int32_t at = 0; at < count; at++)
            sum += a[at];
        return sum;
    }

    // Sixteen sums side by side, four in each of four registers: the additions are reordered, which the compiler
    // may not do to the plain loop by itself.
    float gm_native_float_sum_simd(const float* a, int32_t count)
    {
#if defined(__aarch64__)
        float32x4_t s0 = vdupq_n_f32(0.0f), s1 = s0, s2 = s0, s3 = s0;
        int32_t at = 0;
        for (; at + 16 <= count; at += 16)
        {
            s0 = vaddq_f32(s0, vld1q_f32(a + at));
            s1 = vaddq_f32(s1, vld1q_f32(a + at + 4));
            s2 = vaddq_f32(s2, vld1q_f32(a + at + 8));
            s3 = vaddq_f32(s3, vld1q_f32(a + at + 12));
        }
        float sum = vaddvq_f32(vaddq_f32(vaddq_f32(s0, s1), vaddq_f32(s2, s3)));
        for (; at < count; at++)
            sum += a[at];
        return sum;
#else
        return gm_native_float_sum(a, count);
#endif
    }

    float gm_native_float_dot(const float* a, const float* b, int32_t count)
    {
        float sum = 0.0f;
        for (int32_t at = 0; at < count; at++)
            sum += a[at] * b[at];
        return sum;
    }

    float gm_native_float_dot_simd(const float* a, const float* b, int32_t count)
    {
#if defined(__aarch64__)
        float32x4_t s0 = vdupq_n_f32(0.0f), s1 = s0, s2 = s0, s3 = s0;
        int32_t at = 0;
        for (; at + 16 <= count; at += 16)
        {
            s0 = vfmaq_f32(s0, vld1q_f32(a + at), vld1q_f32(b + at));
            s1 = vfmaq_f32(s1, vld1q_f32(a + at + 4), vld1q_f32(b + at + 4));
            s2 = vfmaq_f32(s2, vld1q_f32(a + at + 8), vld1q_f32(b + at + 8));
            s3 = vfmaq_f32(s3, vld1q_f32(a + at + 12), vld1q_f32(b + at + 12));
        }
        float sum = vaddvq_f32(vaddq_f32(vaddq_f32(s0, s1), vaddq_f32(s2, s3)));
        for (; at < count; at++)
            sum += a[at] * b[at];
        return sum;
#else
        return gm_native_float_dot(a, b, count);
#endif
    }

    void gm_native_saxpy(const float* GM_RESTRICT a, const float* GM_RESTRICT b, float s, float* GM_RESTRICT c, int32_t count)
    {
        for (int32_t at = 0; at < count; at++)
            c[at] = a[at] * s + b[at];
    }

    int32_t gm_native_int_sum(const int32_t* a, int32_t count)
    {
        // Unsigned, so that the sum wraps the way C# does it.
        uint32_t sum = 0;
        for (int32_t at = 0; at < count; at++)
            sum += (uint32_t)a[at];
        return (int32_t)sum;
    }

    int64_t gm_native_int_min_max(const int32_t* a, int32_t count)
    {
        int32_t min = INT32_MAX;
        int32_t max = INT32_MIN;
        for (int32_t at = 0; at < count; at++)
        {
            int32_t value = a[at];
            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }
        return (int64_t)(((uint64_t)(uint32_t)max << 32) | (uint32_t)min);
    }

    int32_t gm_native_index_of(const int32_t* a, int32_t count, int32_t value)
    {
        for (int32_t at = 0; at < count; at++)
        {
            if (a[at] == value)
                return at;
        }
        return -1;
    }

    // Sixteen values at a time: a loop that leaves at the first hit is one the compiler does not vectorize by itself.
    int32_t gm_native_index_of_simd(const int32_t* a, int32_t count, int32_t value)
    {
        int32_t at = 0;
#if defined(__aarch64__)
        const int32x4_t wanted = vdupq_n_s32(value);
        for (; at + 16 <= count; at += 16)
        {
            uint32x4_t e0 = vceqq_s32(vld1q_s32(a + at), wanted);
            uint32x4_t e1 = vceqq_s32(vld1q_s32(a + at + 4), wanted);
            uint32x4_t e2 = vceqq_s32(vld1q_s32(a + at + 8), wanted);
            uint32x4_t e3 = vceqq_s32(vld1q_s32(a + at + 12), wanted);
            if (vmaxvq_u32(vorrq_u32(vorrq_u32(e0, e1), vorrq_u32(e2, e3))) != 0)
                break;
        }
#endif
        for (; at < count; at++)
        {
            if (a[at] == value)
                return at;
        }
        return -1;
    }

    int32_t gm_native_index_of_char(const uint16_t* s, int32_t count, uint16_t c)
    {
        for (int32_t at = 0; at < count; at++)
        {
            if (s[at] == c)
                return at;
        }
        return -1;
    }

    int32_t gm_native_bytes_equal(const uint8_t* a, const uint8_t* b, int32_t count)
    {
        return memcmp(a, b, (size_t)count) == 0 ? 1 : 0;
    }

    void gm_native_copy(const int32_t* a, int32_t* b, int32_t count)
    {
        memcpy(b, a, (size_t)count * sizeof(int32_t));
    }

    void gm_native_transform(const GmFloat3* GM_RESTRICT source, GmFloat3* GM_RESTRICT target, int32_t count, float scale, const GmFloat3* offset)
    {
        const GmFloat3 by = *offset;
        for (int32_t at = 0; at < count; at++)
        {
            target[at].x = source[at].x * scale + by.x;
            target[at].y = source[at].y * scale + by.y;
            target[at].z = source[at].z * scale + by.z;
        }
    }

    // The matrix is sixteen floats in the order of UnityEngine.Matrix4x4's fields: m00, m10, m20, m30, m01, ...
    void gm_native_multiply(const float* matrix, const GmFloat3* GM_RESTRICT source, GmFloat3* GM_RESTRICT target, int32_t count)
    {
        float m[16];
        memcpy(m, matrix, sizeof(m));
        for (int32_t at = 0; at < count; at++)
        {
            const float x = source[at].x, y = source[at].y, z = source[at].z;
            target[at].x = m[0] * x + m[4] * y + m[8] * z + m[12];
            target[at].y = m[1] * x + m[5] * y + m[9] * z + m[13];
            target[at].z = m[2] * x + m[6] * y + m[10] * z + m[14];
        }
    }

    void gm_native_sqrt(const float* GM_RESTRICT a, float* GM_RESTRICT target, int32_t count)
    {
        for (int32_t at = 0; at < count; at++)
            target[at] = sqrtf(a[at]);
    }

    void gm_native_sin(const float* GM_RESTRICT a, float* GM_RESTRICT target, int32_t count)
    {
        for (int32_t at = 0; at < count; at++)
            target[at] = sinf(a[at]);
    }

    int32_t gm_native_div_mod(const int32_t* a, int32_t count, int32_t divisor)
    {
        uint32_t sum = 0;
        for (int32_t at = 0; at < count; at++)
            sum += (uint32_t)(a[at] / divisor + a[at] % divisor);
        return (int32_t)sum;
    }
}

#endif
