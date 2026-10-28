using System;
using System.Reflection;

namespace MortalHelper.Core
{
    // release_1.0.6001.0 added includeStoryLog=true to CreateSaveData. Use the
    // same complete snapshot as the native manual saver on either signature.
    public static class NativeSnapshot
    {
        public static object Create(object service)
        {
            if (service == null) throw new InvalidOperationException("存档服务未就绪。");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = service.GetType();
            var old = type.GetMethod("CreateSaveData", flags, null, Type.EmptyTypes, null);
            if (old != null) return old.Invoke(service, null);
            var current = type.GetMethod("CreateSaveData", flags, null, new[] { typeof(bool) }, null);
            if (current != null) return current.Invoke(service, new object[] { true });
            throw new MissingMethodException(type.FullName, "CreateSaveData() / CreateSaveData(bool)");
        }
    }
}
