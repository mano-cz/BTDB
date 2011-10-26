﻿using System;
using System.Reflection.Emit;

namespace BTDB.FieldHandler
{
    public interface IFieldHandler
    {
        string Name { get; }
        byte[] Configuration { get; }
        bool IsCompatibleWith(Type type, FieldHandlerOptions options);
        Type HandledType();
        bool NeedsCtx();
        void Load(ILGenerator ilGenerator, Action<ILGenerator> pushReaderOrCtx);
        void Skip(ILGenerator ilGenerator, Action<ILGenerator> pushReaderOrCtx);
        void Save(ILGenerator ilGenerator, Action<ILGenerator> pushWriterOrCtx, Action<ILGenerator> pushValue);

        IFieldHandler SpecializeLoadForType(Type type);
        IFieldHandler SpecializeSaveForType(Type type);
    }
}