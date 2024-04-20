// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Sockets;
using Aspire.Hosting.Valkey;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspire.Hosting.Tests.Valkey;

public class AddValkeyTests
{
    [Fact]
    public void AddValkeyContainerWithDefaultsAddsAnnotationMetadata()
    {
        var appBuilder = DistributedApplication.CreateBuilder();
        appBuilder.AddValkey("myValkey").PublishAsContainer();

        using var app = appBuilder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var containerResource = Assert.Single(appModel.Resources.OfType<ValkeyResource>());
        Assert.Equal("myValkey", containerResource.Name);

        var endpoint = Assert.Single(containerResource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(6379, endpoint.TargetPort);
        Assert.False(endpoint.IsExternal);
        Assert.Equal("tcp", endpoint.Name);
        Assert.Null(endpoint.Port);
        Assert.Equal(ProtocolType.Tcp, endpoint.Protocol);
        Assert.Equal("tcp", endpoint.Transport);
        Assert.Equal("tcp", endpoint.UriScheme);

        var containerAnnotation = Assert.Single(containerResource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(ValkeyContainerImageTags.Tag, containerAnnotation.Tag);
        Assert.Equal(ValkeyContainerImageTags.Image, containerAnnotation.Image);
        Assert.Equal(ValkeyContainerImageTags.Registry, containerAnnotation.Registry);
    }

    [Fact]
    public void AddValkeyContainerAddsAnnotationMetadata()
    {
        var appBuilder = DistributedApplication.CreateBuilder();
        appBuilder.AddValkey("myValkey", port: 9813);

        using var app = appBuilder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var containerResource = Assert.Single(appModel.Resources.OfType<ValkeyResource>());
        Assert.Equal("myValkey", containerResource.Name);

        var endpoint = Assert.Single(containerResource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(6379, endpoint.TargetPort);
        Assert.False(endpoint.IsExternal);
        Assert.Equal("tcp", endpoint.Name);
        Assert.Equal(9813, endpoint.Port);
        Assert.Equal(ProtocolType.Tcp, endpoint.Protocol);
        Assert.Equal("tcp", endpoint.Transport);
        Assert.Equal("tcp", endpoint.UriScheme);

        var containerAnnotation = Assert.Single(containerResource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(ValkeyContainerImageTags.Tag, containerAnnotation.Tag);
        Assert.Equal(ValkeyContainerImageTags.Image, containerAnnotation.Image);
        Assert.Equal(ValkeyContainerImageTags.Registry, containerAnnotation.Registry);
    }

    [Fact]
    public async Task ValkeyCreatesConnectionString()
    {
        var appBuilder = DistributedApplication.CreateBuilder();
        appBuilder.AddValkey("myValkey")
            .WithEndpoint("tcp", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 2000));

        using var app = appBuilder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var connectionStringResource = Assert.Single(appModel.Resources.OfType<IResourceWithConnectionString>());
        var connectionString = await connectionStringResource.GetConnectionStringAsync(default);
        Assert.Equal("{myValkey.bindings.tcp.host}:{myValkey.bindings.tcp.port}", connectionStringResource.ConnectionStringExpression.ValueExpression);
        Assert.StartsWith("localhost:2000", connectionString);
    }

    [Fact]
    public async Task VerifyManifest()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("valkey");

        var manifest = await ManifestUtils.GetManifest(valkey.Resource);

        var expectedManifest = $$"""
            {
              "type": "container.v0",
              "connectionString": "{valkey.bindings.tcp.host}:{valkey.bindings.tcp.port}",
              "image": "{{ValkeyContainerImageTags.Registry}}/{{ValkeyContainerImageTags.Image}}:{{ValkeyContainerImageTags.Tag}}",
              "bindings": {
                "tcp": {
                  "scheme": "tcp",
                  "protocol": "tcp",
                  "transport": "tcp",
                  "targetPort": 6379
                }
              }
            }
            """;
        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void WithDataVolumeAddsVolumeAnnotation(bool? isReadOnly)
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey");
        if (isReadOnly.HasValue)
        {
            valkey.WithDataVolume(isReadOnly: isReadOnly.Value);
        }
        else
        {
            valkey.WithDataVolume();
        }

        var volumeAnnotation = valkey.Resource.Annotations.OfType<ContainerMountAnnotation>().Single();

        Assert.Equal("testhost-myValkey-data", volumeAnnotation.Source);
        Assert.Equal("/data", volumeAnnotation.Target);
        Assert.Equal(ContainerMountType.Volume, volumeAnnotation.Type);
        Assert.Equal(isReadOnly ?? false, volumeAnnotation.IsReadOnly);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void WithDataBindMountAddsMountAnnotation(bool? isReadOnly)
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey");
        if (isReadOnly.HasValue)
        {
            valkey.WithDataBindMount("mydata", isReadOnly: isReadOnly.Value);
        }
        else
        {
            valkey.WithDataBindMount("mydata");
        }

        var volumeAnnotation = valkey.Resource.Annotations.OfType<ContainerMountAnnotation>().Single();

        Assert.Equal(Path.Combine(builder.AppHostDirectory, "mydata"), volumeAnnotation.Source);
        Assert.Equal("/data", volumeAnnotation.Target);
        Assert.Equal(ContainerMountType.BindMount, volumeAnnotation.Type);
        Assert.Equal(isReadOnly ?? false, volumeAnnotation.IsReadOnly);
    }

    [Fact]
    public void WithDataVolumeAddsPersistenceAnnotation()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey")
                              .WithDataVolume();

        var persistenceAnnotation = valkey.Resource.Annotations.OfType<ValkeyPersistenceCommandLineArgsCallbackAnnotation>().Single();

        Assert.Equal(TimeSpan.FromSeconds(60), persistenceAnnotation.Interval);
        Assert.Equal(1, persistenceAnnotation.KeysChangedThreshold);
    }

    [Fact]
    public void WithDataVolumeDoesNotAddPersistenceAnnotationIfIsReadOnly()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey")
                           .WithDataVolume(isReadOnly: true);

        var persistenceAnnotation = valkey.Resource.Annotations.OfType<ValkeyPersistenceCommandLineArgsCallbackAnnotation>().SingleOrDefault();

        Assert.Null(persistenceAnnotation);
    }

    [Fact]
    public void WithDataBindMountAddsPersistenceAnnotation()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey")
                           .WithDataBindMount("myvalkeydata");

        var persistenceAnnotation = valkey.Resource.Annotations.OfType<ValkeyPersistenceCommandLineArgsCallbackAnnotation>().Single();

        Assert.Equal(TimeSpan.FromSeconds(60), persistenceAnnotation.Interval);
        Assert.Equal(1, persistenceAnnotation.KeysChangedThreshold);
    }

    [Fact]
    public void WithDataBindMountDoesNotAddPersistenceAnnotationIfIsReadOnly()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey")
                           .WithDataBindMount("myvalkeydata", isReadOnly: true);

        var persistenceAnnotation = valkey.Resource.Annotations.OfType<ValkeyPersistenceCommandLineArgsCallbackAnnotation>().SingleOrDefault();

        Assert.Null(persistenceAnnotation);
    }

    [Fact]
    public void WithPersistenceReplacesPreviousAnnotationInstances()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey")
                           .WithDataVolume()
                           .WithPersistence(TimeSpan.FromSeconds(10), 2);

        var persistenceAnnotation = valkey.Resource.Annotations.OfType<ValkeyPersistenceCommandLineArgsCallbackAnnotation>().Single();

        Assert.Equal(TimeSpan.FromSeconds(10), persistenceAnnotation.Interval);
        Assert.Equal(2, persistenceAnnotation.KeysChangedThreshold);
    }

    [Fact]
    public void WithPersistenceAddsCommandLineArgsAnnotation()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var valkey = builder.AddValkey("myValkey")
                           .WithPersistence(TimeSpan.FromSeconds(60));

        Assert.True(valkey.Resource.TryGetAnnotationsOfType<CommandLineArgsCallbackAnnotation>(out var argsAnnotations));
        Assert.NotNull(argsAnnotations.SingleOrDefault());
    }
}