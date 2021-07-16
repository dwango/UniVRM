using System;
using System.Collections.Generic;

namespace UniGLTF
{
    public enum ErrorLevels
    {
        /// <summary>
        /// Exportできる。お知らせ       
        /// </summary>
        Info,

        /// <summary>
        /// Exportできる。不具合の可能性
        /// </summary>
        Warning,

        /// <summary>
        /// Exportするために修正が必用
        /// </summary>
        Error,

        /// <summary>
        /// Exportの前提を満たさない
        /// </summary>
        Critical,
    }

    public struct ValidationContext
    {
        /// <summary>
        /// Messageの発生個所にジャンプするための情報
        /// </summary>
        public Type Type;
        public UnityEngine.Object Context;

        /// <summary>
        /// DrawGUIから呼び出す。追加のGUIボタンなどを実装する
        /// </summary>
        public Action Extended;

        public static ValidationContext Create<T>(T c) where T : UnityEngine.Object
        {
            return new ValidationContext
            {
                Type = typeof(T),
                Context = c,
            };
        }

        public static ValidationContext Create(Action extended)
        {
            return new ValidationContext
            {
                Extended = extended,
            };
        }
    }

    public struct Validation
    {
        public readonly ErrorLevels ErrorLevel;

        /// <summary>
        /// エクスポート可能か否か
        /// </summary>
        public bool CanExport
        {
            get
            {
                switch (ErrorLevel)
                {
                    case ErrorLevels.Info:
                    case ErrorLevels.Warning:
                        return true;
                    case ErrorLevels.Error:
                    case ErrorLevels.Critical:
                        return false;
                }
                throw new NotImplementedException();
            }
        }

        public readonly String Message;

        public ValidationContext Context;

        Validation(ErrorLevels canExport, string message, ValidationContext context = default)
        {
            ErrorLevel = canExport;
            Message = message;
            Context = context;
        }

        public static Validation Critical(string msg, ValidationContext context = default)
        {
            return new Validation(ErrorLevels.Critical, msg, context);
        }

        public static Validation Error(string msg, ValidationContext context = default)
        {
            return new Validation(ErrorLevels.Error, msg, context);
        }

        public static Validation Warning(string msg, ValidationContext context = default)
        {
            return new Validation(ErrorLevels.Warning, msg, context);
        }

        public static Validation Info(string msg, ValidationContext context = default)
        {
            return new Validation(ErrorLevels.Info, msg);
        }

        public void AddTo(IList<Validation> dst)
        {
            dst.Add(this);
        }
    }
}
