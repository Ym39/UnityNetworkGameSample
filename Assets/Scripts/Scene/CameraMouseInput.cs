using UnityEngine;
using UnityEngine.InputSystem;

public class CameraMouseInput : MonoBehaviour
{
    [SerializeField]
    private Camera _camera;

    [Tooltip("Used when playing offline. In a lobby the local networked character wins.")]
    [SerializeField]
    private CharacterController _controller;

    void Update()
    {
        Vector2 targetPosition;

#if UNITY_EDITOR
        if (Mouse.current == null)
        {
            return;
        }
        
        if (Mouse.current.leftButton.wasPressedThisFrame == false)
        {
            return;
        }
        
        targetPosition = Mouse.current.position.ReadValue();  
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        if (Touchscreen.current == null)
        {
            return;
        }

        if (Touchscreen.current.press.isPressed == false)
        {
            return;
        }

        targetPosition = Touchscreen.current.primaryTouch.position.ReadValue();
#endif

        var ray = _camera.ScreenPointToRay(targetPosition);

        if (Physics.Raycast(ray, out RaycastHit hit) == false)
        {
            return;
        }

        var hitPosition = hit.point;

        // Once in a lobby the order has to go through the network player so the
        // other peers hear about it. Offline it falls back to driving the character
        // directly, which keeps the scene playable without EOS.
        var localPlayer = NetworkGameManager.Instance != null
            ? NetworkGameManager.Instance.LocalPlayer
            : null;

        if (localPlayer != null)
        {
            localPlayer.OrderMove(hitPosition);
        }
        else if (_controller != null)
        {
            _controller.OrderTargetPosition(hitPosition);
        }
    }
}
