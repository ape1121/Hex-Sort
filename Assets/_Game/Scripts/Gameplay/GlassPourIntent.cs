using UnityEngine;

public readonly struct GlassPourIntent
{
    public GlassPourIntent(
        Vector3 pourOrigin,
        Vector3 downhillDirection,
        Vector3 horizontalLeanDirection,
        Vector3 openingNormal,
        float flowReadiness,
        float tiltAngle)
    {
        PourOrigin = pourOrigin;
        DownhillDirection = downhillDirection;
        HorizontalLeanDirection = horizontalLeanDirection;
        OpeningNormal = openingNormal;
        FlowReadiness = flowReadiness;
        TiltAngle = tiltAngle;
    }

    public Vector3 PourOrigin { get; }

    public Vector3 DownhillDirection { get; }

    public Vector3 HorizontalLeanDirection { get; }

    public Vector3 OpeningNormal { get; }

    public float FlowReadiness { get; }

    public float TiltAngle { get; }
}
