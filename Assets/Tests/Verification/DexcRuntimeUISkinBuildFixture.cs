using UnityEngine;
#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
using Dexc;
#endif

namespace DexcDebugger.Verification
{
	public sealed class DexcRuntimeUISkinBuildFixture : MonoBehaviour
	{
#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
		[SerializeField]
		DexcRuntimeUISkin m_Skin;

		[SerializeField]
		DexcRuntimeUISkin m_NotoSansJPPreset;
#endif
	}
}
