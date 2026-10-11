namespace SharpAmp.Core.Audio;

public enum AudioBackend { Mme, Wasapi, Asio, DirectSound }
public enum AudioDirection { Input, Output }

/// <summary>Backend-specific ID: MME index, WASAPI endpoint ID, ASIO driver name, or DirectSound GUID.</summary>
public sealed record AudioDevice(AudioBackend Backend, AudioDirection Direction, string Id, string Name);
