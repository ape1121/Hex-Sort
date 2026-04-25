using UnityEngine;

public readonly struct LiquidDynamicsSample
{
    public LiquidDynamicsSample(Vector3 containerUp, Vector3 downhillDirection, float flowReadiness, float agitation, bool isHeld, bool isPouring)
    {
        ContainerUp = containerUp;
        DownhillDirection = downhillDirection;
        FlowReadiness = flowReadiness;
        Agitation = agitation;
        IsHeld = isHeld;
        IsPouring = isPouring;
    }

    public Vector3 ContainerUp { get; }

    public Vector3 DownhillDirection { get; }

    public float FlowReadiness { get; }

    public float Agitation { get; }

    public bool IsHeld { get; }

    public bool IsPouring { get; }
}
