using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Mvc.Server.Filters
{
    public class ValidateModelFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Allow partial update
            if (!context.ModelState.IsValid && (context.HttpContext.Request.Method == "PATCH" || context.HttpContext.Request.Method == "PUT"))
            {
                // get the errors which only have 'required type' error
                var modelStateErrors = context.ModelState.Where(model =>
                {
                    // ignore only if required error is present for the property
                    if (model.Value.Errors.Count == 1)
                    {
                        // improve code to remove check on hard coded string - "required"
                        // assuming required validation error message contains word "required"
                        return model.Value.Errors.First().ErrorMessage.Contains("required");
                    }
                    return false;
                });
                // remove 'required type' errors from the ModelState
                foreach (var errorModel in modelStateErrors)
                {
                    context.ModelState.Remove(errorModel.Key);
                }

            }
            // Return validation error response
            if (!context.ModelState.IsValid)
            {
                var modelErrors = new Dictionary<string, object>
                {
                    ["message"] = "The request has validation errors.",
                    ["errors"] = new SerializableError(context.ModelState)
                };
                context.Result = new BadRequestObjectResult(modelErrors);
            }
        }
    }
}