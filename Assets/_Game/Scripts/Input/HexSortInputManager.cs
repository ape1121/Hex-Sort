using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

public sealed class HexSortInputManager : MonoBehaviour
{
    private object primaryCaptureOwner;
    private bool previousPrimaryHeld;
    private Vector2 previousPrimaryPosition;
    private bool wasMultiTouchGesture;
    private Vector2 previousGestureCenter;
    private float previousPinchDistance;

    public bool PrimaryPressedThisFrame { get; private set; }

    public bool PrimaryReleasedThisFrame { get; private set; }

    public bool PrimaryIsHeld { get; private set; }

    public Vector2 PrimaryScreenPosition { get; private set; }

    public Vector2 PrimaryScreenDelta { get; private set; }

    public bool MultiTouchGestureActive { get; private set; }

    public bool MultiTouchStartedThisFrame { get; private set; }

    public bool CameraPanActive { get; private set; }

    public Vector2 CameraPanDelta { get; private set; }

    public float ZoomDelta { get; private set; }

    public bool TryCapturePrimary(object owner)
    {
        if (owner == null || primaryCaptureOwner != null || !PrimaryIsHeld || MultiTouchGestureActive)
        {
            return false;
        }

        primaryCaptureOwner = owner;
        return true;
    }

    public bool OwnsPrimary(object owner)
    {
        return owner != null && ReferenceEquals(primaryCaptureOwner, owner);
    }

    public bool IsPrimaryCapturedByOther(object owner)
    {
        return primaryCaptureOwner != null && !ReferenceEquals(primaryCaptureOwner, owner);
    }

    public void ReleasePrimary(object owner)
    {
        if (OwnsPrimary(owner))
        {
            primaryCaptureOwner = null;
        }
    }

    private void Update()
    {
        PollInput();
    }

    private void PollInput()
    {
        PrimaryPressedThisFrame = false;
        PrimaryReleasedThisFrame = false;
        PrimaryIsHeld = false;
        PrimaryScreenPosition = Vector2.zero;
        PrimaryScreenDelta = Vector2.zero;
        MultiTouchGestureActive = false;
        MultiTouchStartedThisFrame = false;
        CameraPanActive = false;
        CameraPanDelta = Vector2.zero;
        ZoomDelta = 0f;

        bool currentPrimaryHeld = false;
        Vector2 currentPrimaryPosition = Vector2.zero;

        if (TryPollTouchInput(out currentPrimaryHeld, out currentPrimaryPosition))
        {
        }
        else
        {
            PollMouseInput(out currentPrimaryHeld, out currentPrimaryPosition);
        }

        PrimaryIsHeld = currentPrimaryHeld;
        PrimaryScreenPosition = currentPrimaryPosition;
        PrimaryPressedThisFrame = currentPrimaryHeld && !previousPrimaryHeld;
        PrimaryReleasedThisFrame = !currentPrimaryHeld && previousPrimaryHeld;

        if (currentPrimaryHeld && previousPrimaryHeld)
        {
            PrimaryScreenDelta = currentPrimaryPosition - previousPrimaryPosition;
        }

        if (!currentPrimaryHeld)
        {
            primaryCaptureOwner = null;
        }

        previousPrimaryHeld = currentPrimaryHeld;
        previousPrimaryPosition = currentPrimaryPosition;
        wasMultiTouchGesture = MultiTouchGestureActive;
    }

    private bool TryPollTouchInput(out bool currentPrimaryHeld, out Vector2 currentPrimaryPosition)
    {
        currentPrimaryHeld = false;
        currentPrimaryPosition = Vector2.zero;

        if (Touchscreen.current == null)
        {
            return false;
        }

        ReadOnlyArray<TouchControl> touches = Touchscreen.current.touches;
        int firstTouchIndex = -1;
        int secondTouchIndex = -1;
        int activeTouchCount = 0;

        for (int i = 0; i < touches.Count; i++)
        {
            if (!touches[i].press.isPressed)
            {
                continue;
            }

            if (firstTouchIndex < 0)
            {
                firstTouchIndex = i;
            }
            else if (secondTouchIndex < 0)
            {
                secondTouchIndex = i;
            }

            activeTouchCount++;
        }

        if (activeTouchCount == 0)
        {
            return false;
        }

        if (activeTouchCount >= 2)
        {
            TouchControl first = touches[firstTouchIndex];
            TouchControl second = touches[secondTouchIndex];

            Vector2 firstPosition = first.position.ReadValue();
            Vector2 secondPosition = second.position.ReadValue();
            Vector2 currentCenter = (firstPosition + secondPosition) * 0.5f;
            float currentDistance = Vector2.Distance(firstPosition, secondPosition);

            MultiTouchGestureActive = true;
            MultiTouchStartedThisFrame = !wasMultiTouchGesture;
            CameraPanActive = true;

            if (wasMultiTouchGesture)
            {
                CameraPanDelta = currentCenter - previousGestureCenter;
                ZoomDelta = (currentDistance - previousPinchDistance) * 0.01f;
            }

            previousGestureCenter = currentCenter;
            previousPinchDistance = currentDistance;
            return true;
        }

        currentPrimaryHeld = true;
        currentPrimaryPosition = touches[firstTouchIndex].position.ReadValue();
        previousGestureCenter = currentPrimaryPosition;
        previousPinchDistance = 0f;
        return true;
    }

    private void PollMouseInput(out bool currentPrimaryHeld, out Vector2 currentPrimaryPosition)
    {
        currentPrimaryHeld = false;
        currentPrimaryPosition = Vector2.zero;

        if (Mouse.current == null)
        {
            return;
        }

        currentPrimaryHeld = Mouse.current.leftButton.isPressed;
        currentPrimaryPosition = Mouse.current.position.ReadValue();

        bool panHeld = Mouse.current.middleButton.isPressed || Mouse.current.rightButton.isPressed;
        if (panHeld)
        {
            CameraPanActive = true;
            CameraPanDelta = Mouse.current.delta.ReadValue();
        }

        ZoomDelta = Mouse.current.scroll.ReadValue().y * 0.012f;
    }
}
