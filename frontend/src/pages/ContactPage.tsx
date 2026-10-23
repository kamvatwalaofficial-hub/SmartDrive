import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { api } from '../api/endpoints'
import { useAuth } from '../auth/AuthContext'
import { Field } from '../components/Field'
import { Alert, PageHeader } from '../components/ui'
import { applyServerErrors } from '../lib/forms'

const schema = z.object({
  name: z.string().trim().min(1, 'Required.').max(100),
  email: z.email('Enter a valid email address.'),
  subject: z.string().trim().min(1, 'Required.').max(150),
  message: z.string().trim().min(10, 'Write at least 10 characters.').max(4000),
})
type Values = z.infer<typeof schema>

export default function ContactPage() {
  const { user } = useAuth()
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { name: user ? `${user.firstName} ${user.lastName}` : '', email: user?.email ?? '', subject: '', message: '' } })
  const { register, handleSubmit, formState: { errors, isSubmitting } } = form

  return (
    <div className="container-page max-w-2xl py-8">
      <PageHeader title="Contact SmartDrive" subtitle="Messages are stored for the admin team. This demo does not send email." />
      {sent ? <Alert tone="success" title="Message received">Thank you — an administrator can see it in the dashboard.</Alert> : (
        <form className="card space-y-4 p-5" noValidate onSubmit={handleSubmit(async (v) => {
          setError(null)
          try { await api.contact(v); setSent(true) } catch (e) { setError(applyServerErrors(e, form.setError, Object.keys(v))) }
        })}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field id="c-name" label="Name" error={errors.name?.message}><input id="c-name" className="input" {...register('name')} /></Field>
            <Field id="c-email" label="Email" error={errors.email?.message}><input id="c-email" type="email" className="input" {...register('email')} /></Field>
          </div>
          <Field id="c-subject" label="Subject" error={errors.subject?.message}><input id="c-subject" className="input" {...register('subject')} /></Field>
          <Field id="c-message" label="Message" error={errors.message?.message}><textarea id="c-message" className="input min-h-36" {...register('message')} /></Field>
          {error && <Alert>{error}</Alert>}
          <button className="btn-primary" disabled={isSubmitting}>{isSubmitting ? 'Sending…' : 'Send message'}</button>
        </form>
      )}
    </div>
  )
}
