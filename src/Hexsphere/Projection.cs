using Godot;

public abstract class Projection
{
	public abstract Vector2 Project(Vector3 X, int face);

	public abstract Vector3 InvProject(Vector2 P, int face);
}
