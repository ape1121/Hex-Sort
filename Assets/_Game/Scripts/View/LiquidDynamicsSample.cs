using UnityEngine;

public readonly struct LiquidDynamicsSample
{
    public LiquidDynamicsSample(
        Vector3 containerUp,
        Vector3 downhillDirection,
        Vector3 linearVelocity,
        float angularSpeedDeg,
        float flowReadiness,
        float agitation,
        bool isHeld,
        bool isPouring)
    {
        ContainerUp = containerUp;
        DownhillDirection = downhillDirection;
        LinearVelocity = linearVelocity;
        AngularSpeedDeg = angularSpeedDeg;
        FlowReadiness = flowReadiness;
        Agitation = agitation;
        IsHeld = isHeld;
        IsPouring = isPouring;
    }

    public Vector3 ContainerUp { get; }

    public Vector3 DownhillDirection { get; }

    public Vector3 LinearVelocity { get; }

    public float AngularSpeedDeg { get; }

    public float FlowReadiness { get; }

    public float Agitation { get; }

    public bool IsHeld { get; }

    public bool IsPouring { get; }
}
