import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import FeedbackForm, { FEEDBACK_FIELD_MAX_LENGTH } from './FeedbackForm'

const LABELS = ['What they are doing well', "What they aren't doing well", 'What they need to improve']

describe('FeedbackForm', () => {
  it('presents exactly the three required fields', () => {
    render(<FeedbackForm onSubmit={vi.fn()} />)

    for (const label of LABELS) {
      const field = screen.getByLabelText(new RegExp(label, 'i'))
      expect(field).toBeRequired()
    }
  })

  it('blocks submission and highlights an empty required field', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<FeedbackForm onSubmit={onSubmit} />)

    // Fill two of the three fields, leave "needs to improve" empty.
    await user.type(screen.getByLabelText(/^what they are doing well/i), 'Great communication.')
    await user.type(screen.getByLabelText(/aren't doing well/i), 'Sometimes misses deadlines.')
    await user.click(screen.getByRole('button', { name: /submit feedback/i }))

    expect(onSubmit).not.toHaveBeenCalled()
    const needsToImprove = screen.getByLabelText(/what they need to improve/i)
    expect(needsToImprove).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('alert')).toHaveTextContent(/required/i)
  })

  it('blocks submission when a field exceeds the character limit', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<FeedbackForm onSubmit={onSubmit} />)

    const tooLong = 'a'.repeat(FEEDBACK_FIELD_MAX_LENGTH + 50)
    const doingWell = screen.getByLabelText(/^what they are doing well/i)
    // Type in one paste-like action rather than key-by-key for test performance.
    await user.click(doingWell)
    await user.paste(tooLong)
    await user.type(screen.getByLabelText(/aren't doing well/i), 'Fine.')
    await user.type(screen.getByLabelText(/what they need to improve/i), 'Fine.')
    await user.click(screen.getByRole('button', { name: /submit feedback/i }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText(`${FEEDBACK_FIELD_MAX_LENGTH + 50} / ${FEEDBACK_FIELD_MAX_LENGTH}`)).toBeInTheDocument()
    expect(screen.getByText(/2000 characters or fewer/i)).toBeInTheDocument()
  })

  it('calls onSubmit with the field values once everything is valid', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<FeedbackForm onSubmit={onSubmit} />)

    await user.type(screen.getByLabelText(/^what they are doing well/i), 'Great communication.')
    await user.type(screen.getByLabelText(/aren't doing well/i), 'Sometimes misses deadlines.')
    await user.type(screen.getByLabelText(/what they need to improve/i), 'Follow up on action items sooner.')
    await user.click(screen.getByRole('button', { name: /submit feedback/i }))

    expect(onSubmit).toHaveBeenCalledWith({
      doingWell: 'Great communication.',
      notDoingWell: 'Sometimes misses deadlines.',
      needsToImprove: 'Follow up on action items sooner.',
    })
  })
})
