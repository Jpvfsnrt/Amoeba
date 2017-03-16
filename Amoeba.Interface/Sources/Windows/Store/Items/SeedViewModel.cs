using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using Amoeba.Core;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Amoeba.Interface
{
    class SeedViewModel : TreeViewModelBase
    {
        private volatile bool _disposed;

        private CompositeDisposable _disposable = new CompositeDisposable();

        public ReactiveProperty<long> Length { get; private set; }
        public ReactiveProperty<DateTime> CreationTime { get; private set; }
        public ReactiveProperty<Metadata> Metadata { get; private set; }

        public SeedInfo Model { get; private set; }

        public SeedViewModel(TreeViewModelBase parent, SeedInfo model)
            : base(parent)
        {
            this.Model = model;

            this.Name = model.ToReactivePropertyAsSynchronized(n => n.Name).AddTo(_disposable);
            this.Length = model.ToReactivePropertyAsSynchronized(n => n.Length).AddTo(_disposable);
            this.CreationTime = model.ToReactivePropertyAsSynchronized(n => n.CreationTime).AddTo(_disposable);
            this.Metadata = model.ToReactivePropertyAsSynchronized(n => n.Metadata).AddTo(_disposable);
        }

        public override string DragFormat { get { return "Store"; } }

        public override bool TryAdd(object value)
        {
            return false;
        }

        public override bool TryRemove(object value)
        {
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _disposable.Dispose();
            }
        }
    }
}