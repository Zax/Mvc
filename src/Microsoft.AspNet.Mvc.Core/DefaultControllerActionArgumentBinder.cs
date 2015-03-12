// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc.Core;
using Microsoft.AspNet.Mvc.ModelBinding;
using Microsoft.Framework.OptionsModel;

namespace Microsoft.AspNet.Mvc
{
    /// <summary>
    /// Provides a default implementation of <see cref="IControllerActionArgumentBinder"/>.
    /// Uses ModelBinding to populate action parameters.
    /// </summary>
    public class DefaultControllerActionArgumentBinder : IControllerActionArgumentBinder
    {
        private readonly IModelMetadataProvider _modelMetadataProvider;
        private readonly MvcOptions _options;
        private readonly IObjectModelValidator _validator;
        private readonly ICompositeMetadataDetailsProvider _compositeDetailsProvider;

        public DefaultControllerActionArgumentBinder(
            IModelMetadataProvider modelMetadataProvider,
            IObjectModelValidator validator,
            ICompositeMetadataDetailsProvider compositeDetailsProvider,
            IOptions<MvcOptions> optionsAccessor)
        {
            _modelMetadataProvider = modelMetadataProvider;
            _options = optionsAccessor.Options;
            _validator = validator;
            _compositeDetailsProvider = compositeDetailsProvider;
        }

        public async Task<IDictionary<string, object>> GetActionArgumentsAsync(
            ActionContext actionContext,
            ActionBindingContext actionBindingContext)
        {
            var actionDescriptor = actionContext.ActionDescriptor as ControllerActionDescriptor;
            if (actionDescriptor == null)
            {
                throw new ArgumentException(
                    Resources.FormatActionDescriptorMustBeBasedOnControllerAction(
                        typeof(ControllerActionDescriptor)),
                        nameof(actionContext));
            }


            var methodParameters = actionDescriptor.MethodInfo.GetParameters();
            var parameterMetadata = new List<MetadataDTO>();
            foreach (var parameter in actionDescriptor.Parameters)
            {
                var parameterInfo = methodParameters.Where(p => p.Name == parameter.Name).Single();
                var metadata = _modelMetadataProvider.GetMetadataForType(parameterInfo.parameterType);

                var allAttributes = new List<object>();
                allAttributes.Add(parameter.BinderMetadata);
                allAttributes.AddRange(parameterInfo.GetCustomAttributes());

                new bindingMetadataProviderContext = new BindingMetadataProviderContext(
                    ModelMetadataIdentity.ForParameter(parameterInfo),
                    allAttributes);
                _compositeDetailsProvider.GetBindingMetadata(bindingMetadataProviderContext);

                var metadataDTO = new MetadataDTO() {
                    ModelMetadata = metadata,
                    BinderMetadata = bindingMetadataProviderContext.BindingMetadata
                };
                parameterMetadata.Add(metadataDTO);
            }

            var actionArguments = new Dictionary<string, object>(StringComparer.Ordinal);
            await PopulateArgumentAsync(actionContext, actionBindingContext, actionArguments, parameterMetadata);
            return actionArguments;
        }

        private async Task PopulateArgumentAsync(
            ActionContext actionContext,
            ActionBindingContext bindingContext,
            IDictionary<string, object> arguments,
            IEnumerable<MetadataDTO> parameterMetadata)
        {
            var operationBindingContext = new OperationBindingContext
            {
                ModelBinder = bindingContext.ModelBinder,
                ValidatorProvider = bindingContext.ValidatorProvider,
                MetadataProvider = _modelMetadataProvider,
                HttpContext = actionContext.HttpContext,
                ValueProvider = bindingContext.ValueProvider,
            };

            var modelState = actionContext.ModelState;
            modelState.MaxAllowedErrors = _options.MaxModelValidationErrors;
            foreach (var parameter in parameterMetadata)
            {
                var parameterType = parameter.ModelMetadata.ModelType;

                var modelBindingContext = GetModelBindingContext(parameter, modelState, operationBindingContext);
                var modelBindingResult = await bindingContext.ModelBinder.BindModelAsync(modelBindingContext);
                if (modelBindingResult != null && modelBindingResult.IsModelSet)
                {
                    var modelExplorer = new ModelExplorer(
                        _modelMetadataProvider,
                        parameter.ModelMetadata,
                        modelBindingResult.Model);

                    arguments[parameter.ModelMetadata.PropertyName] = modelBindingResult.Model;
                    var validationContext = new ModelValidationContext(
                        modelBindingResult.Key,
                        bindingContext.ValidatorProvider,
                        actionContext.ModelState,
                        modelExplorer);
                    _validator.Validate(validationContext);
                }
            }
        }

        // Internal for tests
        internal static ModelBindingContext GetModelBindingContext(
            MetadataDTO metadataDTO,
            BindingMetadata bindingMetadata,
            ModelStateDictionary modelState,
            OperationBindingContext operationBindingContext)
        {
            var modelMetadata = metadataDTO.ModelMetadata;
            var modelBindingContext = ModelBindingContext.GetModelBindingContext(
                modelMetadata,
                metadataDTO.BinderMetadata);

            modelBindingContext.ModelState = modelState;
            modelBindingContext.ValueProvider = operationBindingContext.ValueProvider;
            modelBindingContext.OperationBindingContext = operationBindingContext;

            return modelBindingContext;
        }

        private class MetadataDTO
        {
            public ModelMetadata ModelMetadata { get; set; }

            public BinderMetadata BinderMetadata { get; set; }
        }
    }
}
