using Godot;
using System;

public partial class TestNode : Node
{
    public override void _Ready()
    {

        GD.Print(Icosahedron.b[0][0]);

    }
}
