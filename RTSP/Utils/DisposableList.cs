using System;
using System.Collections.Generic;
using System.Text;

namespace Rtsp.Utils
{
    public sealed class DisposableList : IDisposable
    {
        private readonly List<IDisposable> _disposables;
        private DisposableList(List<IDisposable> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }

        public static IDisposable GetDisposable(List<IDisposable> list)
        {
            if (list.Count ==1)
                return list[0];
            return new DisposableList(list);
        }
    }
}
