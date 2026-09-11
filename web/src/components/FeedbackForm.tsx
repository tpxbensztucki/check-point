import { useState, type FormEvent } from 'react'

export interface FeedbackFormValues {
  doingWell: string
  notDoingWell: string
  needsToImprove: string
}

export const FEEDBACK_FIELD_MAX_LENGTH = 2000

interface FieldConfig {
  key: keyof FeedbackFormValues
  label: string
  helpText: string
}

// Copy chosen to actively encourage constructive, balanced framing (spec Section
// 6) — this is UX guidance, not a validation rule, so it can't be enforced beyond
// the wording itself.
const FIELDS: FieldConfig[] = [
  {
    key: 'doingWell',
    label: 'What they are doing well',
    helpText: 'Be specific — concrete examples land better as genuine recognition.',
  },
  {
    key: 'notDoingWell',
    label: "What they aren't doing well",
    helpText: 'Focus on the behaviour or outcome, not the person — the goal is to help them grow.',
  },
  {
    key: 'needsToImprove',
    label: 'What they need to improve',
    helpText: 'Frame this as an actionable next step they can act on.',
  },
]

const EMPTY_VALUES: FeedbackFormValues = {
  doingWell: '',
  notDoingWell: '',
  needsToImprove: '',
}

export interface FeedbackFormProps {
  onSubmit: (values: FeedbackFormValues) => void
}

// Submitting is validated and blocked here (spec Section 6); actually sending the
// result to the backend is a separate story (Submission handling and
// confirmation, CBLT-233) — the caller's onSubmit is only invoked once every
// field passes.
function FeedbackForm({ onSubmit }: FeedbackFormProps) {
  const [values, setValues] = useState<FeedbackFormValues>(EMPTY_VALUES)
  const [touched, setTouched] = useState<Partial<Record<keyof FeedbackFormValues, boolean>>>({})
  const [attemptedSubmit, setAttemptedSubmit] = useState(false)

  const errors = FIELDS.reduce<Partial<Record<keyof FeedbackFormValues, string>>>((acc, field) => {
    const value = values[field.key]
    if (value.trim().length === 0) {
      acc[field.key] = 'This field is required.'
    } else if (value.length > FEEDBACK_FIELD_MAX_LENGTH) {
      acc[field.key] = `This field must be ${FEEDBACK_FIELD_MAX_LENGTH} characters or fewer.`
    }
    return acc
  }, {})

  function handleChange(key: keyof FeedbackFormValues, value: string) {
    setValues((prev) => ({ ...prev, [key]: value }))
  }

  function handleBlur(key: keyof FeedbackFormValues) {
    setTouched((prev) => ({ ...prev, [key]: true }))
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setAttemptedSubmit(true)
    if (Object.keys(errors).length === 0) {
      onSubmit(values)
    }
  }

  return (
    <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-6">
      {FIELDS.map((field) => {
        const value = values[field.key]
        const error = errors[field.key]
        const showError = Boolean(error) && (touched[field.key] || attemptedSubmit)
        const fieldId = `feedback-field-${field.key}`
        const errorId = `${fieldId}-error`
        const counterId = `${fieldId}-counter`
        const overLimit = value.length > FEEDBACK_FIELD_MAX_LENGTH

        return (
          <div key={field.key} className="flex flex-col gap-1">
            <label htmlFor={fieldId} className="text-sm font-medium text-gray-900">
              {field.label} <span aria-hidden="true" className="text-red-600">*</span>
            </label>
            <p className="text-xs text-gray-500">{field.helpText}</p>
            <textarea
              id={fieldId}
              required
              rows={4}
              value={value}
              onChange={(event) => handleChange(field.key, event.target.value)}
              onBlur={() => handleBlur(field.key)}
              aria-required="true"
              aria-invalid={showError}
              aria-describedby={showError ? `${counterId} ${errorId}` : counterId}
              className={`w-full rounded-md border p-2 text-sm text-gray-900 focus:ring-2 focus:ring-offset-1 focus:outline-none ${
                showError ? 'border-red-500 focus:ring-red-500' : 'border-gray-300 focus:ring-blue-500'
              }`}
            />
            <div className="flex items-center justify-between text-xs">
              <span id={counterId} className={overLimit ? 'font-medium text-red-600' : 'text-gray-400'}>
                {value.length} / {FEEDBACK_FIELD_MAX_LENGTH}
              </span>
              {showError && (
                <span id={errorId} role="alert" className="text-red-600">
                  {error}
                </span>
              )}
            </div>
          </div>
        )
      })}

      <button
        type="submit"
        className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white focus:ring-2 focus:ring-blue-500 focus:ring-offset-1 focus:outline-none"
      >
        Submit feedback
      </button>
    </form>
  )
}

export default FeedbackForm
