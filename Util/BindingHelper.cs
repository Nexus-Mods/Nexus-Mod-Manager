using System;
using System.Windows.Forms;
using System.Linq.Expressions;

namespace Nexus.Client.Util
{
	public static class BindingHelper
	{
		public static Binding CreateManualBinding<T, K>(Control p_ctlControl, Expression<Func<T>> p_expControlProperty, object p_objDataSource, Expression<Func<K>> p_expBoundProperty)
		{
			Binding bdgBinding = new Binding(ObjectHelper.GetPropertyName<T>(p_expControlProperty), p_objDataSource, ObjectHelper.GetPropertyName<K>(p_expBoundProperty), false, DataSourceUpdateMode.Never);
			p_ctlControl.DataBindings.Add(bdgBinding);
			bdgBinding.ControlUpdateMode = ControlUpdateMode.Never;
			return bdgBinding;
		}

		public static Binding CreateManualBinding<T, K>(Control p_ctlControl, Expression<Func<T>> p_expControlProperty, ConvertEventHandler p_cehFormatHandler, object p_objDataSource, Expression<Func<K>> p_expBoundProperty, ConvertEventHandler p_cehParseHandler)
		{
			Binding bdgBinding = new Binding(ObjectHelper.GetPropertyName<T>(p_expControlProperty), p_objDataSource, ObjectHelper.GetPropertyName<K>(p_expBoundProperty), false, DataSourceUpdateMode.Never);
			bdgBinding.Format += p_cehFormatHandler;
			bdgBinding.Parse += p_cehParseHandler;
			p_ctlControl.DataBindings.Add(bdgBinding);
			bdgBinding.ControlUpdateMode = ControlUpdateMode.Never;
			return bdgBinding;
		}

		public static Binding CreateManualBinding<T, K>(Control p_ctlControl, Expression<Func<T, object>> p_expControlProperty, object p_objDataSource, Expression<Func<K, object>> p_expBoundProperty)
		{
			Binding bdgBinding = new Binding(ObjectHelper.GetPropertyName<T>(p_expControlProperty), p_objDataSource, ObjectHelper.GetPropertyName<K>(p_expBoundProperty), false, DataSourceUpdateMode.Never);
			p_ctlControl.DataBindings.Add(bdgBinding);
			bdgBinding.ControlUpdateMode = ControlUpdateMode.Never;
			return bdgBinding;
		}

		public static Binding CreateFullBinding<T, K>(Control p_ctlControl, Expression<Func<T>> p_expControlProperty, object p_objDataSource, Expression<Func<K>> p_expBoundProperty)
		{
			Binding bdgBinding = new Binding(ObjectHelper.GetPropertyName<T>(p_expControlProperty), p_objDataSource, ObjectHelper.GetPropertyName<K>(p_expBoundProperty), true, DataSourceUpdateMode.OnValidation);
			p_ctlControl.DataBindings.Add(bdgBinding);
			return bdgBinding;
		}

		public static Binding CreateFullBinding<T, K>(Control p_ctlControl, Expression<Func<T, object>> p_expControlProperty, object p_objDataSource, Expression<Func<K, object>> p_expBoundProperty)
		{
			Binding bdgBinding = new Binding(ObjectHelper.GetPropertyName<T>(p_expControlProperty), p_objDataSource, ObjectHelper.GetPropertyName<K>(p_expBoundProperty), false, DataSourceUpdateMode.OnValidation);
			p_ctlControl.DataBindings.Add(bdgBinding);
			return bdgBinding;
		}
	}
}
