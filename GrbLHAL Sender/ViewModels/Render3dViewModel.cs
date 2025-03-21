using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Eremex.AvaloniaUI.Controls3D;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace GrbLHAL_Sender.ViewModels;

public class Render3dViewModel : ViewModelBase
{

    private CustomLogger _logger;

    private ObservableCollection<GeometryModel3D> _model = new();

    public Render3dViewModel()
    {
        var model = new GeometryModel3D();
        model.TranslateToZero();
        Model.Add(model);
        Logger = new CustomLogger();
    }
    //static Vertex3D ToVertex3D(ObjVertex vertex, ObjVector3 normal) => new()
    //{
    //    Position = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
    //    Normal = new Vector3(normal.X, normal.Y, normal.Z)
    //};
    //static GeometryModel3D LoadModel(string modelName, string resourceName)
    //{
    //    var assembly = Assembly.GetAssembly(typeof(Graphics3DControlOverviewViewModel));
    //    var stream = assembly!.GetManifestResourceStream(resourceName);
    //    var obj = ObjFile.FromStream(stream);
    //    var vertices = new List<Vertex3D>();
    //    var indices = new List<uint>();
    //    uint index = 0;
    //    foreach (var face in obj.Faces)
    //    {
    //        foreach (var vertex in face.Vertices)
    //        {
    //            vertices.Add(ToVertex3D(obj.Vertices[vertex.Vertex - 1], obj.VertexNormals[vertex.Normal - 1]));
    //            indices.Add(index++);
    //        }
    //    }

    //    return new GeometryModel3D
    //    {
    //        Name = modelName,
    //        Meshes = { new MeshGeometry3D { Vertices = vertices.ToArray(), Indices = indices.ToArray() } }
    //    };
    //}

    public ObservableCollection<GeometryModel3D>Model
    {
        get => _model;
        set => this.RaiseAndSetIfChanged(ref _model, value);
    }

    public CustomLogger Logger
    {
        get => _logger;
        set => this.RaiseAndSetIfChanged(ref _logger, value);
    }


   
}

public class CustomLogger : ObservableObject, ILogger, INotifyPropertyChanged
{
    readonly StringBuilder sb = new();

    public string Text => sb.ToString();

    public event PropertyChangedEventHandler PropertyChanged;

    public IDisposable BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            var message = formatter(state, exception);
            sb.AppendLine($"[{logLevel}] {message}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }
}