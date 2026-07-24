// VfxAutoDestroy.cs
// 轻量运行时组件：挂载在 VfxBake 烘焙出的特效预制体根节点上。
// 当所有非循环粒子系统播放完毕后自动销毁整个 GameObject，
// 便于运行时 Instantiate 即用、无需手动管理生命周期。

using UnityEngine;

namespace MCPForUnity.Runtime.VfxBake
{
    /// <summary>
    /// 粒子特效自动销毁组件：所有非循环 ParticleSystem 播完后 Destroy(gameObject)。
    /// 存在循环（looping）系统时不销毁，由调用方自行管理。
    /// </summary>
    public class VfxAutoDestroy : MonoBehaviour
    {
        // ──────────────────── 内部状态 ────────────────────

        ParticleSystem[] _systems;
        bool _hasPlayed;

        // ──────────────────── 生命周期 ────────────────────

        void Awake()
        {
            _systems = GetComponentsInChildren<ParticleSystem>(true);
        }

        void Update()
        {
            if (_systems == null || _systems.Length == 0)
                return;

            bool anyAlive = false;

            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                if (ps.IsAlive(true))
                    anyAlive = true;
            }

            // 至少有一个系统真正活跃过后才允许销毁，
            // 避免 playOnAwake=false 的预制体在实例化首帧即被销毁。
            if (anyAlive)
                _hasPlayed = true;

            // 循环系统在播放后 IsAlive 恒为 true，天然不会被销毁，
            // 因此无需单独判断 looping。
            if (_hasPlayed && !anyAlive)
                Destroy(gameObject);
        }
    }
}
