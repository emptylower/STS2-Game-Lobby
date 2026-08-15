using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectRitsuLibSidecarCarrier
{
    internal const string ModuleId = "sts2_lan_connect";
    internal const string MessageKey = "protocol_v1";

    private readonly object _sync = new();
    private RitsuBinding? _binding;
    private bool _registrationAttempted;
    private Exception? _lastRegistrationError;

    internal static LanConnectRitsuLibSidecarCarrier Shared { get; } = new();

    internal event Action<ulong, byte[]>? FrameReceived;

    internal bool IsReady => Volatile.Read(ref _binding) != null;

    internal bool TryEnsureRegistered(IEnumerable<Assembly>? assemblies = null)
    {
        if (Volatile.Read(ref _binding) != null)
        {
            return true;
        }

        lock (_sync)
        {
            if (_binding != null)
            {
                return true;
            }

            try
            {
                Assembly? assembly = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
                    .FirstOrDefault(static candidate =>
                        string.Equals(candidate.GetName().Name, "STS2-RitsuLib", StringComparison.OrdinalIgnoreCase));
                if (assembly == null)
                {
                    return false;
                }

                RitsuBinding binding = RitsuBinding.Create(assembly, OnTypedSidecarFrame);
                binding.RegisterAndSubscribe();
                _binding = binding;
                return true;
            }
            catch (Exception exception)
            {
                _lastRegistrationError = exception;
                _registrationAttempted = true;
                return false;
            }
        }
    }

    internal bool HasRegistrationFailureForTesting => _registrationAttempted && _binding == null;

    internal Exception? LastRegistrationErrorForTesting => _lastRegistrationError;

    internal void ObserveNetService(INetGameService? netService)
    {
        if (!TryEnsureRegistered())
        {
            return;
        }

        _binding!.ObserveNetService(netService);
    }

    internal void SetPeerSupported(ulong peerNetId)
    {
        if (!TryEnsureRegistered())
        {
            return;
        }

        _binding!.SetPeerReachability(peerNetId, "Supported");
    }

    internal void SetPeerUnknown(ulong peerNetId)
    {
        if (!TryEnsureRegistered())
        {
            return;
        }

        // RitsuLib v0.5.12 treats an Unknown manual hint as "no verdict" and preserves a prior Supported
        // reachability. Clearing a LAN trusted binding must make future sends fail-closed.
        _binding!.SetPeerReachability(peerNetId, "Unsupported");
    }

    internal bool CanSendToPeer(ulong peerNetId) =>
        TryEnsureRegistered() && _binding!.CanSendToPeer(peerNetId);

    internal bool SendToHost(INetGameService netService, byte[] frame) =>
        TryEnsureRegistered() && _binding!.SendToHost(netService, frame);

    internal bool SendToPeer(INetGameService netService, ulong peerNetId, byte[] frame) =>
        TryEnsureRegistered() && _binding!.SendToPeer(netService, peerNetId, frame);

    internal void ResetForTesting()
    {
        lock (_sync)
        {
            _binding?.Dispose();
            _binding = null;
            _lastRegistrationError = null;
            _registrationAttempted = false;
        }
    }

    private void OnTypedSidecarFrame(ulong senderNetId, byte[] frame)
    {
        FrameReceived?.Invoke(senderNetId, frame);
    }

    private static byte[] SerializePayload(byte[] payload) => payload;

    private static byte[] DeserializePayload(ReadOnlySpan<byte> payload) => payload.ToArray();

    private sealed class RitsuBinding : IDisposable
    {
        private readonly Action<ulong, byte[]> _onFrame;
        private readonly Type _descriptorType;
        private readonly Type _reachabilityType;
        private readonly MethodInfo _register;
        private readonly MethodInfo _subscribe;
        private readonly MethodInfo _sendToHost;
        private readonly MethodInfo _sendToPeer;
        private readonly MethodInfo _observeNetService;
        private readonly MethodInfo _setPeerReachabilityHint;
        private readonly MethodInfo _canSendToPeer;
        private readonly PropertyInfo _contextMessage;
        private readonly PropertyInfo _contextSenderNetId;
        private IDisposable? _subscription;

        private RitsuBinding(
            object descriptor,
            Type descriptorType,
            Type reachabilityType,
            MethodInfo register,
            MethodInfo subscribe,
            MethodInfo sendToHost,
            MethodInfo sendToPeer,
            MethodInfo observeNetService,
            MethodInfo setPeerReachabilityHint,
            MethodInfo canSendToPeer,
            PropertyInfo contextMessage,
            PropertyInfo contextSenderNetId,
            Action<ulong, byte[]> onFrame)
        {
            Descriptor = descriptor;
            _descriptorType = descriptorType;
            _reachabilityType = reachabilityType;
            _register = register;
            _subscribe = subscribe;
            _sendToHost = sendToHost;
            _sendToPeer = sendToPeer;
            _observeNetService = observeNetService;
            _setPeerReachabilityHint = setPeerReachabilityHint;
            _canSendToPeer = canSendToPeer;
            _contextMessage = contextMessage;
            _contextSenderNetId = contextSenderNetId;
            _onFrame = onFrame;
        }

        private object Descriptor { get; }

        internal static RitsuBinding Create(Assembly assembly, Action<ulong, byte[]> onFrame)
        {
            Type descriptorDefinition = RequireType(
                assembly,
                "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarMessageDescriptor`1");
            Type descriptorType = descriptorDefinition.MakeGenericType(typeof(byte[]));
            Type registry = RequireType(
                assembly,
                "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarTypedMessageRegistry");
            Type deliveryType = RequireType(
                assembly,
                "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarDeliverySemantics");
            Type reachabilityType = RequireType(
                assembly,
                "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarPeerReachability");
            Type sessionManager = RequireType(
                assembly,
                "STS2RitsuLib.Networking.Sidecar.RitsuLibSidecarSessionManager");

            ConstructorInfo constructor = descriptorType.GetConstructors()
                .Single(ctor => ctor.GetParameters().Length == 6);
            ParameterInfo[] constructorParameters = constructor.GetParameters();
            Delegate serializer = Delegate.CreateDelegate(
                constructorParameters[2].ParameterType,
                typeof(LanConnectRitsuLibSidecarCarrier).GetMethod(
                    nameof(SerializePayload),
                    BindingFlags.Static | BindingFlags.NonPublic)!);
            Delegate deserializer = Delegate.CreateDelegate(
                constructorParameters[3].ParameterType,
                typeof(LanConnectRitsuLibSidecarCarrier).GetMethod(
                    nameof(DeserializePayload),
                    BindingFlags.Static | BindingFlags.NonPublic)!);
            object delivery = Enum.Parse(deliveryType, "StableSync");
            object descriptor = constructor.Invoke(
                [ModuleId, MessageKey, serializer, deserializer, delivery, true]);

            MethodInfo register = RequireGenericMethod(registry, "Register", 1, 1).MakeGenericMethod(typeof(byte[]));
            MethodInfo subscribe = RequireGenericMethod(registry, "Subscribe", 1, 2).MakeGenericMethod(typeof(byte[]));
            Type contextType = subscribe.GetParameters()[1].ParameterType.GetGenericArguments().Single();
            MethodInfo sendToHost = RequireSendToHost(registry, descriptorType);
            MethodInfo sendToPeer = RequireSendToPeer(registry, descriptorType);
            MethodInfo observeNetService = RequireMethod(sessionManager, "ObserveNetService", 1);
            MethodInfo setPeerReachabilityHint = RequireMethod(sessionManager, "SetPeerReachabilityHint", 2);
            MethodInfo canSendToPeer = RequireMethod(sessionManager, "CanSendToPeer", 1);

            return new RitsuBinding(
                descriptor,
                descriptorType,
                reachabilityType,
                register,
                subscribe,
                sendToHost,
                sendToPeer,
                observeNetService,
                setPeerReachabilityHint,
                canSendToPeer,
                contextType.GetProperty("Message") ?? throw new MissingMemberException(contextType.FullName, "Message"),
                contextType.GetProperty("SenderNetId")
                    ?? throw new MissingMemberException(contextType.FullName, "SenderNetId"),
                onFrame);
        }

        internal void RegisterAndSubscribe()
        {
            _ = _register.Invoke(null, [Descriptor]);
            Type contextType = _contextMessage.DeclaringType!;
            Type handlerType = typeof(Action<>).MakeGenericType(contextType);
            MethodInfo handler = typeof(RitsuBinding)
                .GetMethod(nameof(OnTypedMessage), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(contextType);
            Delegate handlerDelegate = Delegate.CreateDelegate(handlerType, this, handler);
            _subscription = (IDisposable?)_subscribe.Invoke(null, [Descriptor, handlerDelegate])
                ?? throw new InvalidOperationException("RitsuLib Subscribe returned null.");
        }

        internal void ObserveNetService(INetGameService? netService) =>
            _observeNetService.Invoke(null, [netService]);

        internal void SetPeerReachability(ulong peerNetId, string reachability)
        {
            object value = Enum.Parse(_reachabilityType, reachability);
            _setPeerReachabilityHint.Invoke(null, [peerNetId, value]);
        }

        internal bool CanSendToPeer(ulong peerNetId) =>
            _canSendToPeer.Invoke(null, [peerNetId]) is true;

        internal bool SendToHost(INetGameService netService, byte[] frame) =>
            _sendToHost.Invoke(null, [netService, Descriptor, frame]) is true;

        internal bool SendToPeer(INetGameService netService, ulong peerNetId, byte[] frame) =>
            _sendToPeer.Invoke(null, [netService, peerNetId, Descriptor, frame]) is true;

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void OnTypedMessage<TContext>(TContext context)
        {
            object boxed = context!;
            byte[] payload = (byte[])_contextMessage.GetValue(boxed)!;
            ulong senderNetId = (ulong)_contextSenderNetId.GetValue(boxed)!;
            _onFrame(senderNetId, payload);
        }

        private static Type RequireType(Assembly assembly, string fullName) =>
            assembly.GetType(fullName, throwOnError: false)
            ?? throw new TypeLoadException($"RitsuLib public sidecar type is missing: {fullName}");

        private static MethodInfo RequireGenericMethod(Type type, string name, int genericArguments, int parameters) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                    method.Name == name
                    && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == genericArguments
                    && method.GetParameters().Length == parameters);

        private static MethodInfo RequireMethod(Type type, string name, int parameters) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == name && method.GetParameters().Length == parameters);

        private static MethodInfo RequireSendToHost(Type registry, Type descriptorType) =>
            registry.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static method => method.Name == "SendToHost" && method.IsGenericMethodDefinition)
                .Select(method => method.MakeGenericMethod(typeof(byte[])))
                .Single(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 3
                           && typeof(INetGameService).IsAssignableFrom(parameters[0].ParameterType)
                           && parameters[1].ParameterType == descriptorType
                           && parameters[2].ParameterType == typeof(byte[]);
                });

        private static MethodInfo RequireSendToPeer(Type registry, Type descriptorType) =>
            registry.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static method => method.Name == "SendToPeer" && method.IsGenericMethodDefinition)
                .Select(method => method.MakeGenericMethod(typeof(byte[])))
                .Single(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 4
                           && typeof(INetGameService).IsAssignableFrom(parameters[0].ParameterType)
                           && parameters[1].ParameterType == typeof(ulong)
                           && parameters[2].ParameterType == descriptorType
                           && parameters[3].ParameterType == typeof(byte[]);
                });
    }
}
