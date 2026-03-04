using Godot;
using System;

public partial class OrbitCamera : Camera3D
{
    [Export] public float orbitSpeed = 0.5f;
    [Export] public float zoomSpeed = 2.0f;
    [Export] public float minDistance = 15.0f;
    [Export] public float maxDistance = 50.0f;
    [Export] public Vector3 target = Vector3.Zero;

    private float distance = 25.0f;
    private float rotationX = 0.0f;
    private float rotationY = -30.0f;
    private Vector2 lastMousePos;
    private bool isDragging = false;

    public override void _Ready()
    {
        distance = Position.DistanceTo(target);
        UpdateCameraPosition();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                isDragging = mouseButton.Pressed;
                lastMousePos = mouseButton.Position;
                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                distance = Mathf.Max(minDistance, distance - zoomSpeed);
                UpdateCameraPosition();
                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                distance = Mathf.Min(maxDistance, distance + zoomSpeed);
                UpdateCameraPosition();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && isDragging)
        {
            Vector2 delta = mouseMotion.Position - lastMousePos;
            lastMousePos = mouseMotion.Position;

            rotationX -= delta.X * orbitSpeed;
            rotationY = Mathf.Clamp(rotationY - delta.Y * orbitSpeed, -89.0f, 89.0f);

            UpdateCameraPosition();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        // Optional: Auto-rotate
        if (Input.IsKeyPressed(Key.Space))
        {
            rotationX += (float)delta * 20.0f;
            UpdateCameraPosition();
        }
    }

    private void UpdateCameraPosition()
    {
        float radX = Mathf.DegToRad(rotationX);
        float radY = Mathf.DegToRad(rotationY);

        float x = Mathf.Cos(radY) * Mathf.Sin(radX);
        float y = Mathf.Sin(radY);
        float z = Mathf.Cos(radY) * Mathf.Cos(radX);

        Position = target + new Vector3(x, y, z) * distance;
        LookAt(target, Vector3.Up);
    }
}
